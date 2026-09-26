using System.IO.Compression;
using System.Text;
using Clearinet.Compatibility.Extensions;
using Xunit;
using CompatShimHost = Clearinet.CompatShim.CompatShimHost;

namespace Clearinet.ExtensionPorts.Tests;

/// <summary>
/// Eric Lawrence's real FiddlerImportNetlog, ported with nothing but its
/// `using Fiddler;` lines removed (see ../port.ps1), loaded the way the app
/// loads any extension (a .dll in a folder, through <see cref="ExtensionHost"/>)
/// and run against a synthetic NetLog capture. CI runs this on Windows and
/// macOS; passing on both is the parity bar for the import path.
///
/// These tests change <see cref="CompatShimHost"/>'s static hooks, so they
/// all live in this one class (xUnit runs a class's tests one at a time)
/// and put the hooks back afterwards.
/// </summary>
public sealed class NetLogImporterPortTests : IDisposable
{
    private const string FormatName = "NetLog JSON";
    private const string PortedDllName = "CLeARINETNetLog.dll";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "clearinet-port-tests", Guid.NewGuid().ToString("N"));
    private readonly List<string> _log = [];
    private readonly List<(string Title, string Message)> _notices = [];
    private readonly Action<string> _originalLog = CompatShimHost.Log;
    private readonly Action<string, string>? _originalNotify = CompatShimHost.NotifyUser;
    private readonly Func<string, string, string?>? _originalPrompt = CompatShimHost.PromptForOpenFile;
    private readonly ExtensionHost _host;

    public NetLogImporterPortTests()
    {
        CompatShimHost.Log = message => { lock (_log) { _log.Add(message); } };
        CompatShimHost.NotifyUser = (message, title) => { lock (_notices) { _notices.Add((title, message)); } };
        CompatShimHost.PromptForOpenFile = null;

        var extensions = Path.Combine(_folder, "Extensions");
        Directory.CreateDirectory(extensions);
        File.Copy(FindPortedDll(), Path.Combine(extensions, PortedDllName));

        _host = new ExtensionHost([extensions], log: _log.Add);
        _host.Load();
    }

    public void Dispose()
    {
        _host.Unload();
        CompatShimHost.Log = _originalLog;
        CompatShimHost.NotifyUser = _originalNotify;
        CompatShimHost.PromptForOpenFile = _originalPrompt;

        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // The collectible load context may still hold the .dll open on
            // Windows for a moment; a leftover temp folder is harmless.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "netlog-basic.json");

    [Fact]
    public void LoadsThroughExtensionHostAsAnImporter()
    {
        Assert.Empty(_host.LoadErrors);
        var importer = Assert.Single(_host.Importers);

        var format = Assert.Single(ProfferedFormats.Of(importer));
        Assert.Equal(FormatName, format.Name);
        Assert.Equal(".json;.gz", format.Extensions);
    }

    [Fact]
    public void ImportsAnHttp1RequestWithItsBodyHeadersTimingAndFlags()
    {
        var sessions = Import(new Dictionary<string, object> { ["Filename"] = FixturePath });

        var hello = Assert.Single(sessions, s => s.Host == "example.test" && s.Request.Target == "/hello?x=1");
        Assert.Equal("GET", hello.Request.Method);
        Assert.Equal(new[] { "Host", "User-Agent", "Accept" }, hello.Request.Headers.Select(h => h.Name).ToArray());
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1790000000012), hello.StartedAt);

        Assert.Equal(200, hello.Response.StatusCode);
        Assert.Equal("OK", hello.Response.ReasonPhrase);
        Assert.Equal("Hello, NetLog!", Encoding.UTF8.GetString(hello.Response.Body));

        // The NetLog body is already decoded, so the importer renames the
        // encoding header and fixes Content-Length -- through the shim's
        // RenameHeaderItems and indexer.
        Assert.Null(Header(hello.Response.Headers, "Content-Encoding"));
        Assert.Equal("gzip", Header(hello.Response.Headers, "X-Netlog-Removed-Content-Encoding"));
        Assert.Equal("14", Header(hello.Response.Headers, "Content-Length"));
        Assert.Equal("99", Header(hello.Response.Headers, "X-Netlog-Original-Content-Length"));

        Assert.NotNull(hello.Flags);
        Assert.Equal("101", hello.Flags["X-Netlog-URLRequest-ID"]);
        Assert.Equal("FixtureBrowser:0", hello.Flags["X-ProcessInfo"]);
        Assert.Equal("101845102 (blink_resource_loader)", hello.Flags["X-Netlog-Traffic_Annotation"]);
    }

    [Fact]
    public void ImportsAnHttp2RequestWhoseBodiesWereNotCaptured()
    {
        var sessions = Import(new Dictionary<string, object> { ["Filename"] = FixturePath });

        // No Host header in an HTTP/2 capture: the host comes from the
        // absolute URL the importer builds from the pseudo-headers.
        var submit = Assert.Single(sessions, s => s.Host == "api.example.test");
        Assert.Equal("POST", submit.Request.Method);
        Assert.Equal("/submit", submit.Request.Target);
        Assert.Equal("application/json", Header(submit.Request.Headers, "content-type"));

        Assert.Equal(404, submit.Response.StatusCode);
        Assert.Equal("Not Found", submit.Response.ReasonPhrase);
        Assert.Empty(submit.Response.Body);

        Assert.NotNull(submit.Flags);
        Assert.Equal("HTTP2", submit.Flags["X-Transport"]);
        Assert.Equal("5", submit.Flags["X-RequestBodyLength"]);
        Assert.Equal("42", submit.Flags["X-RESPONSEBODYTRANSFERLENGTH"]);
    }

    [Fact]
    public void AddsItsOwnSummarySessionsUnderTheNetlogHost()
    {
        var sessions = Import(new Dictionary<string, object> { ["Filename"] = FixturePath });

        var synthetic = sessions.Where(s => s.Host == "NETLOG").Select(s => s.Request.Target).OrderBy(t => t, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "/CAPTURE_INFO", "/RAW_JSON", "/URL_REQUESTS" }, synthetic);
        Assert.Equal(5, sessions.Count);

        // Built with Session.BuildFromData, then filled in later with
        // utilSetResponseBody -- both shim members.
        var summary = sessions.Single(s => s.Request.Target == "/CAPTURE_INFO");
        var text = Encoding.UTF8.GetString(summary.Response.Body);
        Assert.Contains("FixtureBrowser v1.0", text);
        Assert.Contains("URLRequests:\t\t2 found.", text);
        Assert.Equal(summary.Response.Body.Length.ToString(), Header(summary.Response.Headers, "Content-Length"));
    }

    [Theory]
    [InlineData("gz")]
    [InlineData("zip")]
    public void CompressedCapturesImportTheSame(string wrapping)
    {
        var path = Path.Combine(_folder, "capture.json." + wrapping);
        var json = File.ReadAllBytes(FixturePath);
        if (wrapping == "gz")
        {
            using var file = File.Create(path);
            using var gzip = new GZipStream(file, CompressionLevel.Optimal);
            gzip.Write(json);
        }
        else
        {
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            using var entry = zip.CreateEntry("netlog.json").Open();
            entry.Write(json);
        }

        var sessions = Import(new Dictionary<string, object> { ["Filename"] = path });

        Assert.Equal(
            new[] { "api.example.test/submit", "example.test/hello?x=1" },
            UrlRequests(sessions));
    }

    [Fact]
    public void ImportsFromContentPassedAsAString()
    {
        var sessions = Import(new Dictionary<string, object> { ["Content"] = File.ReadAllText(FixturePath) });

        Assert.Equal(
            new[] { "api.example.test/submit", "example.test/hello?x=1" },
            UrlRequests(sessions));
    }

    [Fact]
    public void ATruncatedCaptureIsRepairedAndTheUserIsWarned()
    {
        // Cut the file partway through its last event line, the way a
        // capture interrupted mid-write ends.
        var text = File.ReadAllText(FixturePath).ReplaceLineEndings("\n");
        var lastEvent = text.LastIndexOf("{\"type\"", StringComparison.Ordinal);
        var path = Path.Combine(_folder, "truncated.json");
        File.WriteAllText(path, text[..(lastEvent + 20)]);

        var sessions = Import(new Dictionary<string, object> { ["Filename"] = path });

        Assert.Contains(_notices, n => n.Title == "Warning" && n.Message.Contains("truncated"));
        Assert.Contains(sessions, s => s.Host == "example.test");
    }

    [Fact]
    public void WithNoFileOrContentItAsksTheHostForAFile()
    {
        string? askedFilter = null;
        CompatShimHost.PromptForOpenFile = (_, filter) =>
        {
            askedFilter = filter;
            return FixturePath;
        };

        var sessions = Import(new Dictionary<string, object>());

        Assert.NotNull(askedFilter);
        Assert.Contains("*.json", askedFilter);
        Assert.Equal(2, UrlRequests(sessions).Length);
    }

    [Fact]
    public void ACancelledFilePromptImportsNothing()
    {
        CompatShimHost.PromptForOpenFile = (_, _) => null;

        Assert.Empty(Import(new Dictionary<string, object>()));
    }

    [Fact]
    public void ProgressIsReportedThroughToCompletion()
    {
        var progress = new List<(float Ratio, string Text)>();

        Import(new Dictionary<string, object> { ["Filename"] = FixturePath }, p => progress.Add((p.CompletionRatio, p.ProgressText)));

        Assert.NotEmpty(progress);
        Assert.Equal(1f, progress[^1].Ratio);
        Assert.Contains(progress, p => p.Text.Contains("Found NetLog v1"));
    }

    private IReadOnlyList<ImportedSession> Import(IReadOnlyDictionary<string, object> options, Action<ProgressCallbackEventArgs>? progress = null)
    {
        var importer = Assert.Single(_host.Importers);
        var sessions = importer.ImportSessions(FormatName, options, progress);
        // The importer catches its own exceptions and reports them through
        // FiddlerApplication.ReportException, which logs them. Fail with the
        // whole logged message so the real cause is visible.
        var failure = _log.FirstOrDefault(line => line.Contains("Failed to import NetLog", StringComparison.Ordinal));
        Assert.True(failure is null, failure);
        return sessions;
    }

    private static string[] UrlRequests(IEnumerable<ImportedSession> sessions) =>
        sessions
            .Where(s => s.Flags is not null && s.Flags.ContainsKey("X-Netlog-URLRequest-ID"))
            .Select(s => s.Host + s.Request.Target)
            .OrderBy(u => u, StringComparer.Ordinal)
            .ToArray();

    private static string? Header(IReadOnlyList<(string Name, string Value)> headers, string name) =>
        headers.Where(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase)).Select(h => h.Value).FirstOrDefault();

    /// <summary>
    /// Where port.ps1 plus the port project put the ported .dll. The
    /// CLEARINET_NETLOG_PORT_DLL environment variable overrides it.
    /// </summary>
    private static string FindPortedDll()
    {
        var overridePath = Environment.GetEnvironmentVariable("CLEARINET_NETLOG_PORT_DLL");
        if (!string.IsNullOrEmpty(overridePath))
        {
            return overridePath;
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, ".work", "bin", "FiddlerImportNetlog", PortedDllName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            "The ported NetLog importer (CLeARINETNetLog.dll) wasn't found. Run tests/ExtensionPorts/port.ps1, then build or test this project (it builds the port first).");
    }
}
