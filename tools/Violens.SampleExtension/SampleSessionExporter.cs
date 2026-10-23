using Violens.Compatibility.Extensions;
using Violens.ProxyCore.Sessions;

namespace Violens.SampleExtension;

/// <summary>
/// An original <see cref="ISessionExporter"/>: writes one line per session to
/// a fixed file beside the DLL. Not a real format; it shows the export
/// path working.
/// </summary>
[ProfferFormat(
    FormatName,
    "Writes a plain-text summary of the exported sessions to SampleExtension-Export.txt next to this extension's own .dll -- validates ExtensionHost's export wiring, not a real export format.")]
public sealed class SampleSessionExporter : ISessionExporter
{
    public const string FormatName = "Sample Text Summary";

    public const string OutputFileName = "SampleExtension-Export.txt";

    public bool ExportSessions(
        string exportFormat, IReadOnlyList<Session> sessions, IReadOnlyDictionary<string, object> options, Action<ProgressCallbackEventArgs>? progress)
    {
        var lines = new List<string>
        {
            $"VioLens SampleExtension export -- {sessions.Count} session(s), written {DateTimeOffset.Now:O}",
        };

        for (var i = 0; i < sessions.Count; i++)
        {
            var session = sessions[i];
            progress?.Invoke(new ProgressCallbackEventArgs((float)i / Math.Max(sessions.Count, 1), $"Session #{session.Id}..."));
            lines.Add($"#{session.Id} {session.Response.StatusCode} {session.Request.Method} https://{session.Host}{session.Request.Target}");
        }

        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, OutputFileName), lines);
        progress?.Invoke(new ProgressCallbackEventArgs(1f, "Done."));
        return true;
    }

    /// <summary>Nothing to release.</summary>
    public void Dispose()
    {
    }
}
