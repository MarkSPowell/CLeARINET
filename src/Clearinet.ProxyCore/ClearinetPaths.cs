namespace Clearinet.ProxyCore;

/// <summary>
/// The one place that answers "where does CLeARINET keep its own per-user
/// data?" -- see the Preferences Design doc's "Location" section.
///
/// Resolves through <see cref="Environment.SpecialFolder.LocalApplicationData"/>,
/// which since .NET 8 maps to <c>%LOCALAPPDATA%</c> on Windows and to
/// <c>~/Library/Application Support</c> on macOS (before .NET 8 it was
/// <c>~/.local/share</c> on macOS -- this project targets net10.0, so that
/// older mapping can't apply, and <c>ClearinetPathsTests</c> asserts the
/// expected parent on each CI leg so a future runtime change would fail CI
/// rather than silently move everyone's settings).
///
/// <c>WinInetSystemProxy</c>, <c>MacOSSystemProxy</c> and
/// <c>FiddlerScriptPreferenceStore</c> still inline the identical
/// expression; moving them onto this is a deliberate follow-up rather than
/// part of the pass that introduced it (two of them are system-proxy
/// crash-recovery code).
/// </summary>
public static class ClearinetPaths
{
    /// <summary>The folder name under the platform's app-data root. Branded, never Fiddler's.</summary>
    public const string AppFolderName = "CLeARINET";

    /// <summary>
    /// <c>%LOCALAPPDATA%\CLeARINET</c> on Windows,
    /// <c>~/Library/Application Support/CLeARINET</c> on macOS. Not created
    /// by reading this property -- callers that write create it themselves.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The platform reported no app-data folder at all. On Unix,
    /// <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/>
    /// returns an empty string rather than throwing when the folder doesn't
    /// exist, and <see cref="Path.Combine(string, string)"/> would then
    /// quietly produce a path relative to whatever the working directory
    /// happens to be -- failing loudly is the better outcome.
    /// </exception>
    public static string AppDataFolder
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(root))
            {
                throw new InvalidOperationException(
                    "The operating system reported no local application-data folder, so CLeARINET has nowhere to keep its settings.");
            }

            return Path.Combine(root, AppFolderName);
        }
    }
}
