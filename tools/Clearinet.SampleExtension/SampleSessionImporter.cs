using Clearinet.Compatibility.Extensions;
using Clearinet.ProxyCore.Http;

namespace Clearinet.SampleExtension;

/// <summary>
/// An original <see cref="ISessionImporter"/> -- proffers one fixed,
/// hardcoded format name and returns two fixed, hardcoded
/// <see cref="ImportedSession"/> records rather than reading any real
/// file. Deliberately has nothing to do with any real capture format: the
/// only thing worth proving here is that <c>ExtensionHost</c> discovers
/// this class, <c>MainWindowViewModel.ImportViaExtension</c> calls it, and
/// the two sessions it returns actually land in the session grid through
/// <c>SessionStore.Add</c> -- the same "prove it through the real call
/// path" approach <c>samples/PhaseA2ValidationRules.js</c> uses for
/// FiddlerScript, just for the import path instead.
/// </summary>
[ProfferFormat(
    FormatName,
    "Two fixed, hardcoded sample sessions -- validates ExtensionHost's import wiring, not a real capture format.")]
public sealed class SampleSessionImporter : ISessionImporter
{
    public const string FormatName = "Sample Fixed Sessions";

    /// <summary>
    /// Ignores <paramref name="importFormat"/> and <paramref name="options"/>
    /// entirely -- there's only one format here, and it needs no settings.
    /// Still calls <paramref name="progress"/>, when given one, so a host
    /// showing progress UI has something real to render.
    /// </summary>
    public IReadOnlyList<ImportedSession> ImportSessions(
        string importFormat, IReadOnlyDictionary<string, object> options, Action<ProgressCallbackEventArgs>? progress)
    {
        progress?.Invoke(new ProgressCallbackEventArgs(0f, "Building sample session 1 of 2..."));
        var first = new ImportedSession(
            "sample.clearinet.invalid",
            DateTimeOffset.Now,
            new CapturedRequest("GET", "/sample-one", "HTTP/1.1", [("Host", "sample.clearinet.invalid")], []),
            new CapturedResponse("HTTP/1.1", 200, "OK", [("Content-Type", "text/plain")], "sample response one"u8.ToArray()));

        progress?.Invoke(new ProgressCallbackEventArgs(0.5f, "Building sample session 2 of 2..."));
        var second = new ImportedSession(
            "sample.clearinet.invalid",
            DateTimeOffset.Now,
            new CapturedRequest(
                "POST", "/sample-two", "HTTP/1.1", [("Host", "sample.clearinet.invalid")], "sample request body"u8.ToArray()),
            new CapturedResponse("HTTP/1.1", 201, "Created", [], []));

        progress?.Invoke(new ProgressCallbackEventArgs(1f, "Done."));
        return [first, second];
    }

    /// <summary>Nothing to release -- no file handle, no connection, nothing held open. Implemented only because <see cref="IDisposable"/> is part of the interface.</summary>
    public void Dispose()
    {
    }
}
