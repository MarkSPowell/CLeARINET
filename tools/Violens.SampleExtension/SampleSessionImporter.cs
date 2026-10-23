using Violens.Compatibility.Extensions;
using Violens.ProxyCore.Http;

namespace Violens.SampleExtension;

/// <summary>
/// An original <see cref="ISessionImporter"/> that returns two fixed
/// sessions, to show the import path working.
/// </summary>
[ProfferFormat(
    FormatName,
    "Two fixed, hardcoded sample sessions -- validates ExtensionHost's import wiring, not a real capture format.")]
public sealed class SampleSessionImporter : ISessionImporter
{
    public const string FormatName = "Sample Fixed Sessions";

    /// <summary>One format, no options; still reports progress.</summary>
    public IReadOnlyList<ImportedSession> ImportSessions(
        string importFormat, IReadOnlyDictionary<string, object> options, Action<ProgressCallbackEventArgs>? progress)
    {
        progress?.Invoke(new ProgressCallbackEventArgs(0f, "Building sample session 1 of 2..."));
        var first = new ImportedSession(
            "sample.violens.invalid",
            DateTimeOffset.Now,
            new CapturedRequest("GET", "/sample-one", "HTTP/1.1", [("Host", "sample.violens.invalid")], []),
            new CapturedResponse("HTTP/1.1", 200, "OK", [("Content-Type", "text/plain")], "sample response one"u8.ToArray()));

        progress?.Invoke(new ProgressCallbackEventArgs(0.5f, "Building sample session 2 of 2..."));
        var second = new ImportedSession(
            "sample.violens.invalid",
            DateTimeOffset.Now,
            new CapturedRequest(
                "POST", "/sample-two", "HTTP/1.1", [("Host", "sample.violens.invalid")], "sample request body"u8.ToArray()),
            new CapturedResponse("HTTP/1.1", 201, "Created", [], []));

        progress?.Invoke(new ProgressCallbackEventArgs(1f, "Done."));
        return [first, second];
    }

    /// <summary>Nothing to release -- no file handle, no connection, nothing held open. Implemented only because <see cref="IDisposable"/> is part of the interface.</summary>
    public void Dispose()
    {
    }
}
