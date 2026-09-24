using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Clearinet.CompatShim;

namespace Clearinet.LegacyExtensionHost;

/// <summary>
/// Entry point for the standalone legacy extension host process. Runs as
/// its own separate window (still, always, its own top-level window -- see
/// the README's unresolved UI-presentation question) with two synthetic
/// demo sessions preloaded for local proving-out, discovery/gating/
/// registration against real, unmodified extension binaries, and running
/// real extension code -- including code reaching into
/// now-removed-on-.NET-5+ WinForms menu/status-bar types -- against this
/// shim for real, once an extension's own AssemblyRef metadata actually
/// points at this shim's own assembly identity (see AssemblyMismatch, and
/// the design doc's "Don't get sued" decision, for why that's no longer
/// automatic for an extension compiled against the real Fiddler assembly,
/// and what a user can do about it).
///
/// Since the session bridge (see <see cref="SessionBridgeServer"/> and the
/// design doc's "Session bridge" section), loaded extensions'
/// <c>IAutoTamper</c> hooks can also run against CLeARINET's own real,
/// live proxied traffic -- not just the two synthetic demo sessions above
/// -- whenever the main app is also running and able to reach this process
/// over the session bridge's named pipe. Still no launch integration or UI
/// merging with the main app: both processes have to already be running
/// for the bridge to connect, and this stays a fully separate window
/// either way.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var mainForm = new frmViewer();

        // Must happen before extensions load -- OnLoad() commonly hooks the
        // host's menu (mnuTools.MenuItems.Add(...) and similar), which
        // needs FiddlerApplication.UI to already point at the real window.
        FiddlerApplication.UI = mainForm;

        // Log to both the on-screen "Log" tab (so this is visible when the
        // .exe is just run directly, no debugger/DebugView needed -- see
        // frmViewer.AppendLog) and Debug.WriteLine (still useful for anyone
        // who does have a debugger attached).
        void Log(string message)
        {
            mainForm.AppendLog(message);
            System.Diagnostics.Debug.WriteLine("[LegacyExtensionHost] " + message);
        }

        Log($"Legacy extensions folder: {DefaultExtensionsFolder}");

        var loader = new LegacyExtensionLoader(new[] { DefaultExtensionsFolder }, Log);
        loader.Load();

        // Starts the session bridge's pipe listener (see SessionBridgeServer
        // and the design doc's "Session bridge" section) regardless of
        // whether anything mismatched or loaded cleanly above -- the main
        // app's own bridge client probes this on every proxy Start() and
        // treats "reachable but zero AutoTampers loaded" as a clean no-op,
        // the same way it treats "not reachable at all." Started after
        // loader.Load() so AutoTampers already reflects whatever actually
        // loaded (or didn't) by the time the first bridge call can arrive.
        SessionBridgeServer.Start(loader.AutoTampers, Log);

        if (loader.LoadErrors.Count > 0)
        {
            foreach (var error in loader.LoadErrors)
            {
                Log(error);
            }

            // Surface the problem immediately rather than leaving it for
            // Mark to notice the Tools menu is empty and go hunting.
            mainForm.SelectLogTab();
        }
        else
        {
            Log("No load errors.");
        }

        // A pop-up dialog specifically for assembly-identity mismatches
        // (see AssemblyMismatch and the design doc's "Don't get sued"
        // decision) -- explaining why an extension didn't load is more
        // useful surfaced here than buried in the Log tab as one more
        // generic load error, so this category gets its own dialog. See
        // AssemblyMismatch.ToDiagnosticMessage's own remarks for why its
        // wording is deliberately restrained (states what's wrong and the
        // one remediation this host stands behind -- recompiling from
        // source -- without walking through retargeting a compiled
        // binary's own metadata). Shown once, after the whole scan, rather
        // than once per mismatched extension, so a folder with several
        // unmodified real extensions in it doesn't stack up a wall of
        // dialogs the person has to click through one at a time.
        if (loader.AssemblyMismatches.Count > 0)
        {
            var summary = string.Join(Environment.NewLine + Environment.NewLine, loader.AssemblyMismatches.Select(m => m.ToDiagnosticMessage()));
            MessageBox.Show(
                mainForm,
                summary,
                loader.AssemblyMismatches.Count == 1
                    ? "1 extension is not compatible with this host"
                    : $"{loader.AssemblyMismatches.Count} extensions are not compatible with this host",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        Log($"IAutoTamper instances registered: {loader.AutoTampers.Count}. " +
            $"IHandleExecAction instances registered: {loader.ExecActionHandlers.Count}.");
        Log($"mnuTools now has {mainForm.mnuTools.MenuItems.Count} item(s), mnuRules has {mainForm.mnuRules.MenuItems.Count} item(s) " +
            "-- an extension's OnLoad() typically adds its Tools/Rules menu entries here, so 0 here (with no load errors above) " +
            "usually means the extension loaded but its OnLoad() didn't add a menu item the way this shim expected.");

        Application.ApplicationExit += (_, _) => loader.Unload();

        Application.Run(mainForm);
    }

    /// <summary>
    /// CLeARINET-branded folder, matching the naming convention
    /// <c>Clearinet.Compatibility.Extensions.ExtensionHost.DefaultExtensionsFolder</c>
    /// already uses for the main app's own (source-compatible) extensions
    /// folder -- kept separate ("LegacyExtensions", not "Extensions") since
    /// a .NET 10 extension dropped in this folder by mistake would fail to
    /// load here (wrong runtime entirely), and vice versa.
    /// </summary>
    private static string DefaultExtensionsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CLeARINET", "LegacyExtensions");
}
