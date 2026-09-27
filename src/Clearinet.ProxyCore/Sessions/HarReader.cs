using System.Globalization;
using System.Text;
using System.Text.Json;
using Clearinet.ProxyCore.Http;

namespace Clearinet.ProxyCore.Sessions;

/// <summary>
/// Imports an HTTP Archive (HAR 1.2) -- from a browser's developer tools,
/// another proxy, or <see cref="HarWriter"/> -- into a <see cref="SessionStore"/>.
///
/// HAR stores bodies decoded, so imported messages don't carry the
/// Content-Encoding or Transfer-Encoding they were sent with (those headers
/// are dropped and Content-Length is set to the body's real length).
/// HTTP/2 pseudo-headers (<c>:authority</c> and friends), which browsers
/// include, are dropped too; the request target becomes the URL's path and
/// query, with a Host header from the URL when the entry has none. An entry
/// that can't be read is skipped and reported, not fatal. An entry's
/// <c>comment</c> becomes the session's comment (the <c>ui-comments</c> flag).
/// </summary>
public static class HarReader
{
    public static async Task<HarImportResult> ImportAsync(
        string path, SessionStore sessionStore, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        return await ImportAsync(stream, sessionStore, cancellationToken);
    }

    public static async Task<HarImportResult> ImportAsync(
        Stream source, SessionStore sessionStore, CancellationToken cancellationToken = default)
    {
        using var document = await JsonDocument.ParseAsync(
            source,
            new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip },
            cancellationToken);

        if (!document.RootElement.TryGetProperty("log", out var log) ||
            !log.TryGetProperty("entries", out var entries) ||
            entries.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("This isn't a HAR file: there's no log.entries list.");
        }

        var imported = 0;
        var skipped = new List<string>();
        var index = 0;
        foreach (var entry in entries.EnumerateArray())
        {
            index++;
            try
            {
                var (host, startedAt, request, response, flags) = ReadEntry(entry);
                sessionStore.Add(host, startedAt, request, response, flags);
                imported++;
            }
            catch (Exception ex) when (ex is InvalidDataException or FormatException or InvalidOperationException or KeyNotFoundException or UriFormatException)
            {
                skipped.Add($"entry {index}: {ex.Message}");
            }
        }

        return new HarImportResult(imported, skipped);
    }

    private static (string Host, DateTimeOffset StartedAt, CapturedRequest Request, CapturedResponse Response, IReadOnlyDictionary<string, string>? Flags)
        ReadEntry(JsonElement entry)
    {
        var harRequest = Required(entry, "request");
        var harResponse = Required(entry, "response");

        var url = StringValue(harRequest, "url");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new InvalidDataException($"the request URL \"{url}\" isn't absolute");
        }

        var requestHeaders = Headers(harRequest);
        if (ContentDecoder.FindHeader(requestHeaders, "Host") is null)
        {
            requestHeaders.Insert(0, ("Host", uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}"));
        }

        var requestBody = harRequest.TryGetProperty("postData", out var postData) && postData.ValueKind == JsonValueKind.Object
            ? Body(postData, "text")
            : Array.Empty<byte>();
        var request = new CapturedRequest(
            StringValue(harRequest, "method", "GET"),
            uri.PathAndQuery,
            HttpVersion(StringValue(harRequest, "httpVersion", "HTTP/1.1")),
            WithLength(requestHeaders, requestBody),
            requestBody);

        var responseBody = harResponse.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Object
            ? Body(content, "text")
            : Array.Empty<byte>();
        var response = new CapturedResponse(
            HttpVersion(StringValue(harResponse, "httpVersion", "HTTP/1.1")),
            harResponse.TryGetProperty("status", out var status) && status.TryGetInt32(out var code) ? code : 0,
            StringValue(harResponse, "statusText"),
            WithLength(Headers(harResponse), responseBody),
            responseBody);

        var startedAt = DateTimeOffset.TryParse(
            StringValue(entry, "startedDateTime"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : DateTimeOffset.Now;

        var comment = StringValue(entry, "comment");
        IReadOnlyDictionary<string, string>? flags = comment.Length > 0
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ui-comments"] = comment }
            : null;

        return (uri.Host, startedAt, request, response, flags);
    }

    private static JsonElement Required(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : throw new InvalidDataException($"no \"{name}\" object");

    private static string StringValue(JsonElement parent, string name, string fallback = "") =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    /// <summary>A body from a HAR <c>content</c> or <c>postData</c> object: base64 when it says so, else UTF-8 text.</summary>
    private static byte[] Body(JsonElement holder, string textProperty)
    {
        var text = StringValue(holder, textProperty);
        if (text.Length == 0)
        {
            return [];
        }

        return string.Equals(StringValue(holder, "encoding"), "base64", StringComparison.OrdinalIgnoreCase)
            ? Convert.FromBase64String(text)
            : Encoding.UTF8.GetBytes(text);
    }

    private static List<(string Name, string Value)> Headers(JsonElement message)
    {
        var headers = new List<(string Name, string Value)>();
        if (!message.TryGetProperty("headers", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return headers;
        }

        foreach (var header in array.EnumerateArray())
        {
            var name = StringValue(header, "name");
            if (name.Length == 0 || name.StartsWith(':') ||
                string.Equals(name, "Content-Encoding", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            headers.Add((name, StringValue(header, "value")));
        }

        return headers;
    }

    private static List<(string Name, string Value)> WithLength(List<(string Name, string Value)> headers, byte[] body)
    {
        if (body.Length > 0)
        {
            headers.Add(("Content-Length", body.Length.ToString(CultureInfo.InvariantCulture)));
        }

        return headers;
    }

    /// <summary>HAR's version strings ("HTTP/1.1", "h2", "http/2.0", "h3") as a start-line version.</summary>
    private static string HttpVersion(string version) => version.ToLowerInvariant() switch
    {
        "h2" or "http/2" or "http/2.0" => "HTTP/2",
        "h3" or "http/3" or "http/3.0" => "HTTP/3",
        "" or "unknown" => "HTTP/1.1",
        _ => version.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase) ? version.ToUpperInvariant() : "HTTP/1.1",
    };
}

/// <summary>How many entries a HAR import added, and why any were skipped.</summary>
public sealed record HarImportResult(int Imported, IReadOnlyList<string> Skipped);
