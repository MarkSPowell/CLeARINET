using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Violens.CompatShim;

namespace Violens.LegacyExtensionHost;

/// <summary>
/// The legacy extension host: a separate WinForms process that loads
/// Fiddler Classic extensions rebuilt against <c>Violens.CompatShim</c>,
/// including ones using WinForms types removed from .NET 5+. Its window has
/// two demo sessions; the session bridge runs extensions' IAutoTamper hooks
/// on the main app's real traffic.
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

        // To the Log tab and to Debug.WriteLine.
        void Log(string message)
        {
            mainForm.AppendLog(message);
            System.Diagnostics.Debug.WriteLine("[LegacyExtensionHost] " + message);
        }

        Log($"Legacy extensions folder: {DefaultExtensionsFolder}");

        var loader = new LegacyExtensionLoader(new[] { DefaultExtensionsFolder }, Log);
        loader.Load();

        // Started even if nothing loaded: the app treats "no AutoTampers" the
        // same as "no host". After Load(), so AutoTampers is complete.
        SessionBridgeServer.Start(loader.AutoTampers, Log);

        if (loader.LoadErrors.Count > 0)
        {
            foreach (var error in loader.LoadErrors)
            {
                Log(error);
            }

            // Show the problem straight away, rather than leave an empty Tools
            // menu to puzzle over.
            mainForm.SelectLogTab();
        }
        else
        {
            Log("No load errors.");
        }

        // Mismatches get one dialog after the scan, since they're the likeliest
        // reason an extension didn't load.
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
    /// "LegacyExtensions" in Documents\VioLens, kept apart from the main app's
    /// "Extensions", since each host's extensions can't load in the other.
    /// </summary>
    private static string DefaultExtensionsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VioLens", "LegacyExtensions");
}
