using Clearinet.ProxyCore.Preferences;

namespace Clearinet.CompatShim;

/// <summary>
/// The handful of things a Fiddler-shaped extension can ask its host to do
/// that only the host app knows how to do: log, show a message, pick a
/// file, store preferences. The desktop app wires these up once at startup
/// (see <c>App.axaml.cs</c>); tests and headless hosts can leave the
/// defaults, which log to the console, never block, and keep preferences in
/// memory.
///
/// <b>Why a namespace shared with the legacy host's shim.</b> This layer
/// lives in <c>Clearinet.CompatShim</c> -- the same namespace the optional,
/// Windows-only legacy extension host's own <c>net48</c> shim uses -- on
/// purpose: porting a Fiddler Classic extension's source is then the same
/// change whichever host it's destined for. The two are separate assemblies
/// that never load into the same process. Neither assembly nor namespace
/// contains the word "Fiddler" (see the .NET Extension Compatibility Design
/// doc's "Don't get sued" section); a ported extension that wants to keep
/// its own <c>Fiddler.Parser</c>-style qualified names adds a
/// <c>global using Fiddler = Clearinet.CompatShim;</c> alias in its own
/// project -- see the Extension Test Targets doc.
///
/// Every member in this namespace was written from public sources only:
/// Telerik's published FiddlerCore API reference and fiddlerbook.com object
/// model pages for signatures and documented behavior, plus the call sites
/// in the ported extensions' own public source. No Fiddler source or binary
/// was read. See CONTRIBUTING.md's clean-room policy.
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
    /// expected, not a CLeARINET version.
    /// </summary>
    public static Version FiddlerApiLevel { get; } = new(5, 0, int.MaxValue, int.MaxValue);
}
