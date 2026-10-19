namespace Violens.DesktopUi.ViewModels;

/// <summary>
/// Every preference name the desktop app reads or writes -- the "What gets
/// persisted" table in the Preferences Design doc, in code. The same names
/// on Windows and macOS; the legacy-host key is simply inert on macOS,
/// where that host can't run.
///
/// Deliberately absent: breakpoint conditions and the AutoResponder's
/// on/off switch. Both change what every app on the machine receives once
/// VioLens is the system proxy, so silently re-arming them at launch
/// would be a trap -- see the design doc's "What is deliberately not
/// persisted" section.
/// </summary>
internal static class PreferenceKeys
{
    public const string PortAutomatic = "violens.proxy.port.auto";
    public const string Port = "violens.proxy.port";

    public const string ShowFiddlerScriptPanel = "violens.ui.panels.fiddlerscript";
    public const string ShowExtensionsPanel = "violens.ui.panels.extensions";
    public const string ShowLegacyExtensionHostPanel = "violens.ui.panels.legacyhost";
    public const string ShowAlsoBreakOnRow = "violens.ui.panels.alsobreakon";

    public const string FilterText = "violens.ui.filter.text";

    public const string FiddlerScriptPath = "violens.fiddlerscript.path";
    public const string AutoLaunchLegacyHost = "violens.extensions.legacyhost.autolaunch";

    public const string ShowConnectionsRow = "violens.ui.panels.connections";
    public const string UpstreamProxy = "violens.proxy.upstream";
    public const string AllowRemoteClients = "violens.proxy.allowremote";

    /// <summary>The user name for an upstream proxy that asks for a sign-in. The password is never saved.</summary>
    public const string UpstreamProxyUserName = "violens.proxy.upstream.username";

    /// <summary>The main window's placement: position in screen pixels, size in the window's own units.</summary>
    public const string WindowLeft = "violens.ui.window.left";
    public const string WindowTop = "violens.ui.window.top";
    public const string WindowWidth = "violens.ui.window.width";
    public const string WindowHeight = "violens.ui.window.height";
    public const string WindowMaximized = "violens.ui.window.maximized";

    /// <summary>
    /// Session-list columns the user hid, by key (see
    /// MainWindowViewModel.Columns.cs): the header for built-in columns,
    /// <c>script:</c>Title and <c>flag:</c>Name for script and extension
    /// columns. Empty means every column is shown.
    /// </summary>
    public const string HiddenSessionColumns = "violens.ui.sessionlist.hiddencolumns";

    /// <summary>
    /// The session list's column order and widths, one <c>width|key</c>
    /// entry per column in display order (see ColumnLayout). Unset means
    /// the order and widths in MainWindow.axaml.
    /// </summary>
    public const string SessionColumnLayout = "violens.ui.sessionlist.columns";

    /// <summary>TextWizard's last conversion and character encoding, by the names it shows.</summary>
    public const string TextWizardTransform = "violens.ui.textwizard.transform";
    public const string TextWizardEncoding = "violens.ui.textwizard.encoding";

    /// <summary>The Import/Export via Extension picker's last choice (see FormatChoice.Key), highlighted next time.</summary>
    public const string LastImportFormat = "violens.extensions.import.lastformat";
    public const string LastExportFormat = "violens.extensions.export.lastformat";
}
