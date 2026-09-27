using Clearinet.ProxyCore.Sessions;

namespace Clearinet.DesktopUi.ViewModels;

/// <summary>File &gt; Import HAR and Export HAR (see <see cref="HarReader"/> and <see cref="HarWriter"/>).</summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>Whether there's anything to export, for greying out Export HAR.</summary>
    public bool HasSessions => Sessions.Count > 0;

    public async Task ImportHarAsync(string path)
    {
        try
        {
            var result = await HarReader.ImportAsync(path, _sessionStore);
            StatusText = result.Skipped.Count == 0
                ? $"Imported {result.Imported} session(s) from {Path.GetFileName(path)}."
                : $"Imported {result.Imported} session(s) from {Path.GetFileName(path)} " +
                  $"({result.Skipped.Count} skipped -- see the console for details).";

            foreach (var reason in result.Skipped)
            {
                Console.WriteLine($"[import] Skipped {reason}");
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to import {Path.GetFileName(path)}: {ex.Message}";
        }
    }

    public async Task ExportHarAsync(string path)
    {
        var sessions = _sessionStore.Snapshot();
        try
        {
            await Task.Run(() => HarWriter.Write(path, sessions));
            StatusText = $"Saved {sessions.Count} session(s) to {path}";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to save HAR: {ex.Message}";
        }
    }
}
