using Clearinet.Compatibility.Extensions;
using Clearinet.ProxyCore.Sessions;

namespace Clearinet.SampleExtension;

/// <summary>
/// An original <see cref="ISessionExporter"/> -- writes a plain-text
/// one-line-per-session summary to a fixed file next to this extension's
/// own <c>.dll</c> (<see cref="AppContext.BaseDirectory"/>), rather than
/// anywhere the host would need to choose. Deliberately not a real export
/// format: proves <c>ExtensionHost</c> discovers this class and
/// <c>MainWindowViewModel.ExportViaExtension</c> calls it with the real,
/// currently-captured sessions -- see <see cref="ISessionImporter"/>'s own
/// remarks (this class's counterpart) for why an extension is expected to
/// pick its own destination rather than receive one through
/// <c>options</c>.
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
            $"CLeARINET SampleExtension export -- {sessions.Count} session(s), written {DateTimeOffset.Now:O}",
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

    /// <summary>Nothing to release -- see <see cref="SampleSessionImporter.Dispose"/>'s own remarks.</summary>
    public void Dispose()
    {
    }
}
