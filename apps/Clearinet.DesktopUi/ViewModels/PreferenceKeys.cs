namespace Clearinet.DesktopUi.ViewModels;

/// <summary>
/// Every preference name the desktop app reads or writes -- the "What gets
/// persisted" table in the Preferences Design doc, in code. The same names
/// on Windows and macOS; the legacy-host key is simply inert on macOS,
/// where that host can't run.
///
/// Deliberately absent: breakpoint conditions and the AutoResponder's
/// on/off switch. Both change what every app on the machine receives once
/// CLeARINET is the system proxy, so silently re-arming them at launch
/// would be a trap -- see the design doc's "What is deliberately not
/// persisted" section.
/// </summary>
internal static class PreferenceKeys
{
    public const string PortAutomatic = "clearinet.proxy.port.auto";
    public const string Port = "clearinet.proxy.port";

    public const string ShowFiddlerScriptPanel = "clearinet.ui.panels.fiddlerscript";
    public const string ShowExtensionsPanel = "clearinet.ui.panels.extensions";
    public const string ShowLegacyExtensionHostPanel = "clearinet.ui.panels.legacyhost";
    public const string ShowAlsoBreakOnRow = "clearinet.ui.panels.alsobreakon";

    public const string FilterText = "clearinet.ui.filter.text";

    public const string FiddlerScriptPath = "clearinet.fiddlerscript.path";
    public const string AutoLaunchLegacyHost = "clearinet.extensions.legacyhost.autolaunch";

    /// <summary>The Import/Export via Extension picker's last choice (see FormatChoice.Key), highlighted next time.</summary>
    public const string LastImportFormat = "clearinet.extensions.import.lastformat";
    public const string LastExportFormat = "clearinet.extensions.export.lastformat";
}
