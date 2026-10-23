using Violens.ProxyCore.Preferences;

namespace Violens.CompatShim;

/// <summary>
/// What a Fiddler-style extension asks of its host: logging, messages, file
/// pickers and preferences. The desktop app sets these at startup; the
/// defaults log to the console, never block, and keep preferences in memory.
///
/// The namespace, <c>Violens.CompatShim</c>, is shared with the legacy
/// host's net48 shim, so porting an extension is the same change for either
/// host. Neither name contains "Fiddler"; a ported extension can add
/// <c>global using Fiddler = Violens.CompatShim;</c> to keep its qualified
/// names. Written from Telerik's published API reference and the ported
/// extensions' own source only (see CONTRIBUTING.md).
/// </summary>
public static class CompatShimHost
{
    /// <summary>Where <see cref="FiddlerApplication.Log"/> and every shim diagnostic goes.</summary>
    public static Action<string> Log { get; set; } = message => Console.WriteLine($"[Extension] {message}");

    /// <summary>Backs <see cref="FiddlerApplication.Prefs"/>. The desktop app points this at its real <see cref="PreferenceStore"/>.</summary>
    public static IPreferenceStore Preferences { get; set; } = PreferenceStore.CreateInMemory();

    /// <summary>
    /// Backs <see cref="Utilities.ObtainOpenFilename(string, string)"/>:
    /// (dialog title, WinForms-style filter) → chosen path, or null if
    /// cancelled. Always called off the UI thread (imports run in the
    /// background), so an implementation may block while a dialog is up.
    /// Null means "no UI available": the call returns null, the same as a
    /// cancelled dialog.
    /// </summary>
    public static Func<string, string, string?>? PromptForOpenFile { get; set; }

    /// <summary>Backs <see cref="FiddlerApplication.DoNotifyUser(string, string)"/>: (message, title). Null means log it instead.</summary>
    public static Action<string, string>? NotifyUser { get; set; }

    /// <summary>
    /// The Fiddler Classic API level this layer stands in for, compared
    /// against a ported extension's <c>[assembly: RequiredVersion("...")]</c>.
    /// Any 5.0.x is accepted: Fiddler Classic's last line was 5.0, and an
    /// extension's declared minimum describes the Fiddler features it
    /// expected, not a VioLens version.
    /// </summary>
    public static Version FiddlerApiLevel { get; } = new(5, 0, int.MaxValue, int.MaxValue);
}
