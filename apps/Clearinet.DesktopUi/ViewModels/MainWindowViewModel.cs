using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using Avalonia.Threading;
using Clearinet.Compatibility.Extensions;
using Clearinet.Compatibility.FiddlerScript;
using Clearinet.DesktopUi.Models;
using Clearinet.Extensibility.Inspection;
using Clearinet.ProxyCore.AutoResponder;
using Clearinet.ProxyCore.Breakpoints;
using Clearinet.ProxyCore.Certificates;
using Clearinet.ProxyCore.Extensions;
using Clearinet.ProxyCore.Preferences;
using Clearinet.ProxyCore.Proxy;
using Clearinet.ProxyCore.Sessions;
using Clearinet.ProxyCore.SystemProxy;

namespace Clearinet.DesktopUi.ViewModels;

/// <summary>
/// Hosts the same proxy core Clearinet.DevHost's console spike proved out
/// in Phase 1 (root CA, leaf certificate provider, session store,
/// intercepting listener) and projects <see cref="SessionStore"/>'s live
/// change feed into an <see cref="ObservableCollection{T}"/> the session
/// grid binds to. Unlike the first cut of this app, the proxy no longer
/// starts itself on launch -- Start/Stop are explicit commands, and the
/// port is editable up until Start is pressed, so a taken port (or just a
/// preference) doesn't require editing code and rebuilding.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private const int DefaultPreferredPort = 8888;

    private readonly SessionStore _sessionStore = new();
    private readonly InspectorRegistry _inspectorRegistry;
    private readonly BreakpointManager _breakpointManager = new();
    private readonly Dictionary<PendingBreakpoint, PendingBreakpointViewModel> _pendingBreakpointViewModels = [];
    private readonly AutoResponderRules _autoResponderRules = new();

    /// <summary>
    /// One long-lived runner, same lifecycle reasoning as
    /// <see cref="_breakpointManager"/>/<see cref="_autoResponderRules"/>
    /// above -- constructed once here (the composition root) and handed to
    /// <see cref="InterceptingProxyListener"/> as
    /// <c>Clearinet.ProxyCore.Scripting.IFiddlerScriptRunner</c> on every
    /// <see cref="Start"/>. Its own <c>Load</c>/<c>Reload</c> can be called
    /// at any time, including while the proxy is running, the same way
    /// <see cref="AutoResponderRules.Rules"/> can already be edited live.
    /// </summary>
    private readonly FiddlerScriptRunner _fiddlerScriptRunner;

    /// <summary>
    /// Unlike <see cref="_fiddlerScriptRunner"/>, loaded exactly once, in
    /// this constructor, and never reloaded -- see <see cref="ExtensionHost"/>'s
    /// own remarks on why compiled .NET extensions don't get FiddlerScript's
    /// live-reload treatment. Its <see cref="ExtensionHost.AutoTampers"/> are
    /// wrapped fresh (via <see cref="ExtensionHost.CreateAutoTamperHost"/>)
    /// and handed to <see cref="InterceptingProxyListener"/> on every
    /// <see cref="Start"/>, the same way <see cref="_fiddlerScriptRunner"/>
    /// is; its inspectors are folded into <see cref="_inspectorRegistry"/>
    /// once, below, since that registry itself is rebuilt fresh here rather
    /// than mutated later.
    /// </summary>
    private readonly ExtensionHost _extensionHost;

    /// <summary>
    /// Manages the optional, opt-in launch/stop lifecycle of
    /// <c>Clearinet.LegacyExtensionHost.exe</c> -- see
    /// <see cref="AutoLaunchLegacyHost"/> for the opt-in gate, and
    /// <see cref="LegacyExtensionHostLauncher"/>'s own remarks for why this
    /// is opt-in rather than automatic. One instance for the app's whole
    /// lifetime, the same reasoning as <see cref="_fiddlerScriptRunner"/>:
    /// it needs to remember, across a <see cref="Stop"/> call, whether it
    /// was the one that actually launched the process, so <see cref="Stop"/>
    /// never closes a legacy host someone else started.
    /// </summary>
    private readonly LegacyExtensionHostLauncher _legacyExtensionHostLauncher = new();

    /// <summary>
    /// Only ever invoked on macOS, only once per <see cref="_authority"/>
    /// (re-asked on a later <see cref="Start"/> click if declined, since
    /// <see cref="_authority"/> stays null until it's accepted) --
    /// see the design doc's "silent-install tension" section for why this
    /// exists at all: unlike Windows' native trust-install dialog, the
    /// shelled-out <c>security</c> command CLeARINET's own
    /// <c>MacOSCertificateTrust</c> uses has no OS-level confirmation of
    /// its own. Supplied by <c>App.axaml.cs</c> (a real modal dialog,
    /// synchronously blocked on -- see that class's own remarks); null
    /// here means "always decline," which is the safe default for any
    /// caller (tests, tooling) that never supplies one -- see
    /// <see cref="Start"/>'s own use of it.
    /// </summary>
    private readonly Func<bool>? _confirmMacOSCertificateTrust;

    /// <summary>
    /// Where every setting in <see cref="PreferenceKeys"/> is read from at
    /// startup and written back to on change -- see the Preferences Design
    /// doc. Owned by this view model (flushed and disposed in
    /// <see cref="Dispose"/>, which runs from <c>ShutdownRequested</c> on
    /// both Windows and macOS). Writes from the setters below are cheap: the
    /// store updates memory immediately and batches the actual file write
    /// on a short background timer, so a keystroke in the filter box never
    /// waits on disk.
    /// </summary>
    private readonly PreferenceStore _preferences;

    /// <summary>
    /// Shows the Import/Export via Extension format picker: (heading,
    /// choices, key of the choice to highlight) → the chosen one, or null if
    /// cancelled. Supplied by App.axaml.cs as FormatPickerWindow, the same
    /// "view model asks through a delegate, never references a window"
    /// pattern as <see cref="_confirmMacOSCertificateTrust"/>. Null means
    /// no picker: see <see cref="PickFormatAsync"/>.
    /// </summary>
    private readonly Func<string, IReadOnlyList<FormatChoice>, string?, Task<FormatChoice?>>? _chooseFormat;

    private CertificateAuthority? _authority;
    private InterceptingProxyListener? _proxy;
    private string _statusText;
    private decimal? _preferredPort = DefaultPreferredPort;
    private bool _isRunning;
    private SessionRow? _selectedSessionRow;
    private PendingBreakpointViewModel? _selectedPendingBreakpoint;
    private AutoResponderRuleViewModel? _selectedAutoResponderRule;
    private SessionQuery _query = SessionQuery.MatchAll;
    private string _filterText = string.Empty;
    private bool _useAutomaticPort = true;
    private bool _isCaptureFlashing;
    private string _fiddlerScriptPath = string.Empty;
    private string _fiddlerScriptStatus = "No script loaded.";
    private string _extensionStatus = "Not scanned yet.";
    private bool _autoLaunchLegacyHost;
    private string _legacyExtensionHostStatus = "Auto-launch is off -- Tools -> Legacy Extension Host to turn it on, then Start.";
    private bool _showFiddlerScriptPanel;
    private bool _showExtensionsPanel;
    private bool _showLegacyExtensionHostPanel;
    private bool _showAlsoBreakOnRow;

    /// <summary>
    /// Restarted (not just started) on every captured session -- see
    /// <see cref="FlashCaptureIndicator"/> -- so a burst of traffic keeps
    /// the flash dot lit continuously instead of blinking once per session,
    /// and it only goes dark shortly after the capture actually goes quiet.
    /// </summary>
    private readonly DispatcherTimer _captureFlashTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    /// <summary>
    /// Every session captured this run, in capture order, regardless of
    /// <see cref="FilterText"/> -- still what <see cref="SaveSazCommand"/>
    /// exports from and what its can-execute check counts, since exporting
    /// only the currently-filtered view would silently drop sessions the
    /// person just hasn't gone looking for. The grid itself binds to
    /// <see cref="FilteredSessions"/>, not this.
    /// </summary>
    public ObservableCollection<SessionRow> Sessions { get; } = [];

    /// <summary>
    /// Tabs extensions added with <see cref="ExtensionUi.AddTab"/>, shown
    /// beside the Inspectors (see MainWindow.axaml.cs). Filled while
    /// extensions load, and never changes after that.
    /// </summary>
    public ObservableCollection<ExtensionTabViewModel> ExtensionTabs { get; } = [];

    /// <summary>
    /// The subset of <see cref="Sessions"/> that <see cref="FilterText"/>
    /// currently matches, in capture order -- what the session grid
    /// actually binds to. Kept as its own collection rather than an
    /// <c>ICollectionView</c>-style live filter over <see cref="Sessions"/>
    /// because Avalonia's DataGrid has nothing built in for that; see
    /// <see cref="ApplyFilter"/> and the SessionAdded handler below for how
    /// the two are kept in sync without re-scanning the whole list on every
    /// single captured session.
    /// </summary>
    public ObservableCollection<SessionRow> FilteredSessions { get; } = [];

    /// <summary>
    /// The live text in the filter box -- see <see cref="SessionQuery"/>
    /// for the grammar (free text plus <c>method:</c>/<c>host:</c>/
    /// <c>status:</c>). Re-parses and rebuilds <see cref="FilteredSessions"/>
    /// on every change; that's a full O(n) rescan of <see cref="Sessions"/>
    /// per keystroke, which is fine at the sizes a single capture run
    /// reaches in practice and simpler than debouncing or incrementally
    /// diffing a changed predicate.
    /// </summary>
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetField(ref _filterText, value))
            {
                _query = SessionQuery.Parse(value);
                ApplyFilter();
                _preferences.SetStringPref(PreferenceKeys.FilterText, value ?? string.Empty);
            }
        }
    }

    /// <summary>
    /// Sessions currently paused at a breakpoint, oldest first. A
    /// connection's processing thread is genuinely blocked for as long as
    /// its entry sits here -- see BreakpointManager's remarks on why its
    /// events fire off that thread, which is why every handler below
    /// marshals through <see cref="Dispatcher"/> the same way
    /// SessionStore.SessionAdded already has to.
    /// </summary>
    public ObservableCollection<PendingBreakpointViewModel> PendingBreakpoints { get; } = [];

    /// <summary>
    /// Whether the breakpoints panel should take up any space at all. True
    /// once either Automatic Breakpoints checkbox is armed, or any of the
    /// narrower bpu/bpafter/bpm/bps rules below has a value -- see
    /// <see cref="BreakpointRules.AnyActive"/>, which this defers to
    /// directly rather than repeating its five-way check here -- so the
    /// panel is there before it's needed rather than popping in only after
    /// the first hit; also true whenever something is actually paused
    /// (<see cref="PendingBreakpoints"/> non-empty) even if every rule got
    /// cleared afterward, since hiding the panel then would hide the only
    /// way to Resume or Abort a connection that's still genuinely blocked.
    /// Recomputed on demand rather than cached -- see every setter/handler
    /// below that calls <c>RaisePropertyChanged</c> for this -- since
    /// nothing here tracks dependencies automatically (this app's MVVM base
    /// is hand-rolled; see ViewModelBase's own remarks).
    /// </summary>
    public bool IsBreakpointsPanelVisible =>
        _breakpointManager.Rules.AnyActive || PendingBreakpoints.Count > 0;

    public PendingBreakpointViewModel? SelectedPendingBreakpoint
    {
        get => _selectedPendingBreakpoint;
        set => SetField(ref _selectedPendingBreakpoint, value);
    }

    /// <summary>Fiddler's "Rules -&gt; Automatic Breakpoints -&gt; Before Requests".</summary>
    public bool BreakOnAllRequests
    {
        get => _breakpointManager.Rules.BreakOnAllRequests;
        set
        {
            if (_breakpointManager.Rules.BreakOnAllRequests == value)
            {
                return;
            }

            _breakpointManager.Rules.BreakOnAllRequests = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(IsBreakpointsPanelVisible));
        }
    }

    /// <summary>Fiddler's "Rules -&gt; Automatic Breakpoints -&gt; After Responses".</summary>
    public bool BreakOnAllResponses
    {
        get => _breakpointManager.Rules.BreakOnAllResponses;
        set
        {
            if (_breakpointManager.Rules.BreakOnAllResponses == value)
            {
                return;
            }

            _breakpointManager.Rules.BreakOnAllResponses = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(IsBreakpointsPanelVisible));
        }
    }

    /// <summary>Fiddler's <c>bpu</c>: pause a request whose target contains this text.</summary>
    public string? RequestUrlContains
    {
        get => _breakpointManager.Rules.RequestUrlContains;
        set
        {
            if (_breakpointManager.Rules.RequestUrlContains == value)
            {
                return;
            }

            _breakpointManager.Rules.RequestUrlContains = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(IsBreakpointsPanelVisible));
        }
    }

    /// <summary>Fiddler's <c>bpafter</c>: pause a response whose request target contains this text.</summary>
    public string? ResponseUrlContains
    {
        get => _breakpointManager.Rules.ResponseUrlContains;
        set
        {
            if (_breakpointManager.Rules.ResponseUrlContains == value)
            {
                return;
            }

            _breakpointManager.Rules.ResponseUrlContains = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(IsBreakpointsPanelVisible));
        }
    }

    /// <summary>Fiddler's <c>bpm</c>: pause a request with this HTTP method.</summary>
    public string? RequestMethodEquals
    {
        get => _breakpointManager.Rules.RequestMethodEquals;
        set
        {
            if (_breakpointManager.Rules.RequestMethodEquals == value)
            {
                return;
            }

            _breakpointManager.Rules.RequestMethodEquals = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(IsBreakpointsPanelVisible));
        }
    }

    /// <summary>
    /// Fiddler's <c>bps</c>: pause a response with this status code.
    /// Nullable decimal to match <c>NumericUpDown.Value</c>'s own type --
    /// the same convention <see cref="PreferredPort"/> already uses for the
    /// same reason -- converted to and from <see cref="BreakpointRules.ResponseStatusCodeEquals"/>'s
    /// <c>int?</c> at this property's boundary rather than pushing the UI's
    /// numeric type down into ProxyCore.
    /// </summary>
    public decimal? ResponseStatusCodeEquals
    {
        get => _breakpointManager.Rules.ResponseStatusCodeEquals;
        set
        {
            var statusCode = value.HasValue ? (int?)value.Value : null;
            if (_breakpointManager.Rules.ResponseStatusCodeEquals == statusCode)
            {
                return;
            }

            _breakpointManager.Rules.ResponseStatusCodeEquals = statusCode;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(IsBreakpointsPanelVisible));
        }
    }

    /// <summary>
    /// Whether the "Also break on" row (<see cref="RequestUrlContains"/>/
    /// <see cref="ResponseUrlContains"/>/<see cref="RequestMethodEquals"/>/
    /// <see cref="ResponseStatusCodeEquals"/>) shows on the main screen at
    /// all -- the checkable "_Tools -&gt; Also Break On" entry is this
    /// property's only writer, same reasoning and default
    /// (<see langword="false"/>) as <see cref="ShowFiddlerScriptPanel"/>.
    /// Purely a visibility switch: none of the four fields above are
    /// cleared when this goes back to <see langword="false"/>, so a
    /// condition set while the row was visible keeps arming
    /// <see cref="IsBreakpointsPanelVisible"/> and keeps pausing real
    /// traffic even after the row that set it is hidden again.
    /// </summary>
    public bool ShowAlsoBreakOnRow
    {
        get => _showAlsoBreakOnRow;
        set
        {
            if (SetField(ref _showAlsoBreakOnRow, value))
            {
                _preferences.SetBoolPref(PreferenceKeys.ShowAlsoBreakOnRow, value);
            }
        }
    }

    /// <summary>
    /// Named to avoid colliding with <see cref="AutoResponderRules"/> (the
    /// ProxyCore engine type) -- one <see cref="AutoResponderRuleViewModel"/>
    /// per <see cref="AutoResponderRules.Rules"/> entry, kept in the same
    /// order by hand (see <see cref="AddAutoResponderRule"/>/
    /// <see cref="RemoveSelectedAutoResponderRule"/>/
    /// <see cref="MoveSelectedAutoResponderRule"/>), the same two-lists
    /// tradeoff <see cref="PendingBreakpoints"/> already makes against
    /// <c>BreakpointManager</c>'s own state.
    /// </summary>
    public ObservableCollection<AutoResponderRuleViewModel> AutoResponderRuleRows { get; } = [];

    /// <summary>
    /// The AutoResponder panel's own on/off switch -- Fiddler Classic's
    /// "Enable rules" checkbox. See <see cref="AutoResponderRules.IsEnabled"/>'s
    /// remarks for why this defaults to <see langword="false"/> unlike the
    /// breakpoints panel's always-live rules.
    /// </summary>
    public bool AutoResponderEnabled
    {
        get => _autoResponderRules.IsEnabled;
        set
        {
            if (_autoResponderRules.IsEnabled == value)
            {
                return;
            }

            _autoResponderRules.IsEnabled = value;
            RaisePropertyChanged();
        }
    }

    public AutoResponderRuleViewModel? SelectedAutoResponderRule
    {
        get => _selectedAutoResponderRule;
        set
        {
            if (SetField(ref _selectedAutoResponderRule, value))
            {
                RemoveAutoResponderRuleCommand.RaiseCanExecuteChanged();
                MoveAutoResponderRuleUpCommand.RaiseCanExecuteChanged();
                MoveAutoResponderRuleDownCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RelayCommand AddAutoResponderRuleCommand { get; }
    public RelayCommand RemoveAutoResponderRuleCommand { get; }
    public RelayCommand MoveAutoResponderRuleUpCommand { get; }
    public RelayCommand MoveAutoResponderRuleDownCommand { get; }

    /// <summary>
    /// The path last typed into the FiddlerScript panel -- not necessarily
    /// what's actually loaded yet; that's <see cref="FiddlerScriptRunner.LoadedPath"/>,
    /// read back into <see cref="FiddlerScriptStatus"/> only once
    /// <see cref="LoadFiddlerScriptCommand"/> actually runs. Kept as its own
    /// field/property (rather than binding the TextBox straight at
    /// something on <see cref="_fiddlerScriptRunner"/>) purely so the
    /// "Load"/"Reload" buttons have a plain string to enable/disable
    /// against without the runner needing any UI-facing surface of its own.
    /// </summary>
    public string FiddlerScriptPath
    {
        get => _fiddlerScriptPath;
        set
        {
            if (SetField(ref _fiddlerScriptPath, value))
            {
                LoadFiddlerScriptCommand.RaiseCanExecuteChanged();
                _preferences.SetStringPref(PreferenceKeys.FiddlerScriptPath, value ?? string.Empty);
            }
        }
    }

    /// <summary>
    /// What the FiddlerScript panel's status line shows: which handlers the
    /// loaded script defines, or its load error -- see
    /// <see cref="RefreshFiddlerScriptStatus"/>. Never read back into any
    /// proxy behavior; purely informational, the same role
    /// <see cref="StatusText"/> plays for the proxy itself.
    /// </summary>
    public string FiddlerScriptStatus
    {
        get => _fiddlerScriptStatus;
        private set => SetField(ref _fiddlerScriptStatus, value);
    }

    /// <summary>
    /// What the extensions status line shows: every folder <see cref="ExtensionHost.Load"/>
    /// scanned (whether it existed, how many .dll's it found), plus what got
    /// loaded and any load errors. Built once, right after <see cref="ExtensionHost.Load"/>
    /// runs in the constructor -- extensions don't come and go mid-run (see
    /// ExtensionHost's own remarks), so unlike <see cref="FiddlerScriptStatus"/>
    /// this never needs to be refreshed later. Exists specifically so this
    /// information doesn't depend on the console being visible at all: a
    /// WinExe app's Console.WriteLine output isn't reliably visible in every
    /// terminal a person might launch it from, and this line is always on
    /// screen in the running app regardless.
    /// </summary>
    public string ExtensionStatus
    {
        get => _extensionStatus;
        private set => SetField(ref _extensionStatus, value);
    }

    /// <summary>
    /// This app's own opt-in switch for whether <see cref="Start"/> should
    /// try to launch <c>Clearinet.LegacyExtensionHost.exe</c> itself (via
    /// <see cref="_legacyExtensionHostLauncher"/>) before probing the
    /// session bridge, rather than requiring a person to have already
    /// started it by hand. Defaults to <see langword="false"/> -- see
    /// <see cref="LegacyExtensionHostLauncher"/>'s own remarks on why this
    /// is opt-in, not automatic. Purely a switch: flipping it doesn't launch
    /// or stop anything by itself, only the next <see cref="Start"/>/
    /// <see cref="Stop"/> click does.
    /// </summary>
    public bool AutoLaunchLegacyHost
    {
        get => _autoLaunchLegacyHost;
        set
        {
            if (SetField(ref _autoLaunchLegacyHost, value))
            {
                _preferences.SetBoolPref(PreferenceKeys.AutoLaunchLegacyHost, value);
            }
        }
    }

    /// <summary>
    /// What the Legacy Extension Host panel's status line shows --
    /// <see cref="LegacyExtensionHostLauncher.Result.Message"/> from the
    /// most recent <see cref="Start"/> call (when <see cref="AutoLaunchLegacyHost"/>
    /// was on), a note that auto-launch is off (when it wasn't), or
    /// <see cref="LegacyExtensionHostLauncher"/>'s own stop-side log line
    /// folded in after <see cref="Stop"/>. Purely informational, the same
    /// role <see cref="ExtensionStatus"/> plays for in-process compiled
    /// extensions.
    /// </summary>
    public string LegacyExtensionHostStatus
    {
        get => _legacyExtensionHostStatus;
        private set => SetField(ref _legacyExtensionHostStatus, value);
    }

    public RelayCommand LoadFiddlerScriptCommand { get; }
    public RelayCommand ReloadFiddlerScriptCommand { get; }

    /// <summary>
    /// Whether the FiddlerScript panel shows on the main screen at all --
    /// the checkable "_Tools -&gt; FiddlerScript" entry in MainWindow.axaml
    /// is this property's only writer (see this project's "move panels into
    /// the Tools menu" UI pass). Defaults to <see langword="false"/>: unlike
    /// the Breakpoints/AutoResponder panels, this one has no other signal
    /// (an armed rule, an active pause) to decide it should be on screen by
    /// default, and hiding it by default is the whole point of moving it off
    /// the always-visible main screen in the first place. Purely a
    /// visibility switch -- loading/reloading a script still works the same
    /// whether or not this panel happens to be shown.
    /// </summary>
    public bool ShowFiddlerScriptPanel
    {
        get => _showFiddlerScriptPanel;
        set
        {
            if (SetField(ref _showFiddlerScriptPanel, value))
            {
                _preferences.SetBoolPref(PreferenceKeys.ShowFiddlerScriptPanel, value);
            }
        }
    }

    /// <summary>
    /// Whether the Extensions status panel shows on the main screen --
    /// the checkable "_Tools -&gt; Extensions" entry is this property's only
    /// writer, same reasoning and default (<see langword="false"/>) as
    /// <see cref="ShowFiddlerScriptPanel"/>. Purely a visibility switch:
    /// <see cref="ExtensionStatus"/> itself is still built once at startup
    /// regardless of whether anyone ever checks this box.
    /// </summary>
    public bool ShowExtensionsPanel
    {
        get => _showExtensionsPanel;
        set
        {
            if (SetField(ref _showExtensionsPanel, value))
            {
                _preferences.SetBoolPref(PreferenceKeys.ShowExtensionsPanel, value);
            }
        }
    }

    /// <summary>
    /// Whether the Legacy Extension Host panel shows on the main screen --
    /// the checkable "_Tools -&gt; Legacy Extension Host" entry is this
    /// property's only writer, same reasoning and default
    /// (<see langword="false"/>) as <see cref="ShowExtensionsPanel"/>.
    /// Purely a visibility switch: <see cref="AutoLaunchLegacyHost"/> and
    /// <see cref="LegacyExtensionHostStatus"/> both work the same whether or
    /// not this happens to be checked.
    /// </summary>
    public bool ShowLegacyExtensionHostPanel
    {
        get => _showLegacyExtensionHostPanel;
        set
        {
            if (SetField(ref _showLegacyExtensionHostPanel, value))
            {
                _preferences.SetBoolPref(PreferenceKeys.ShowLegacyExtensionHostPanel, value);
            }
        }
    }

    /// <summary>
    /// The dynamic "_Rules" menu's own entries -- one per
    /// <c>RulesMenuOption</c>/<c>RulesStringChoice</c> the currently-loaded
    /// script declares (empty when nothing's loaded, or the loaded script
    /// declares none). Rebuilt from scratch by <see cref="RefreshScriptMenus"/>
    /// every time a script (re)loads -- see <c>MainWindow.axaml</c>'s own
    /// remarks on why this binds as one flat <c>MenuItem.ItemsSource</c>
    /// list rather than real nested Rules-menu submenus.
    /// </summary>
    public ObservableCollection<RulesMenuEntryViewModel> RulesMenuEntries { get; } = [];

    /// <summary>
    /// Whether <see cref="RulesMenuEntries"/> currently has anything in it
    /// -- MainWindow.axaml binds the "_Rules" menu's own <c>IsEnabled</c>
    /// to this, so it visibly greys out instead of just opening to an
    /// empty dropdown (nothing was drawing an arrow or reacting to a click
    /// either way, but a disabled menu at least reads as "nothing here
    /// right now" rather than "broken"). Not backed by a field: this app's
    /// MVVM base has no dependency tracking of its own (see
    /// ViewModelBase's own remarks), so <see cref="RefreshScriptMenus"/>
    /// has to explicitly raise this alongside <see cref="RulesMenuEntries"/>
    /// itself whenever that collection is rebuilt.
    /// </summary>
    public bool HasRulesMenuEntries => RulesMenuEntries.Count > 0;

    /// <summary>
    /// One per <c>ToolsAction</c> the currently-loaded script declares.
    /// Same rebuild timing as <see cref="RulesMenuEntries"/>. Despite the
    /// name (kept for the underlying <c>ToolsAction</c> attribute/Phase C
    /// terminology, and because <see cref="MainWindowViewModel.InvokeContextAction"/>
    /// and <see cref="FiddlerScriptRunner.InvokeToolsAction"/> both already
    /// use it), this no longer renders as a "_Tools" submenu -- see the
    /// FiddlerScript panel's own remarks in MainWindow.axaml for why it was
    /// moved to a row of buttons inside that panel instead.
    /// </summary>
    public ObservableCollection<ActionMenuEntryViewModel> ToolsMenuEntries { get; } = [];

    /// <summary>
    /// Whether <see cref="ToolsMenuEntries"/> currently has anything in it,
    /// same reasoning and same "explicitly raised, not field-backed" caveat
    /// as <see cref="HasRulesMenuEntries"/> -- what the FiddlerScript
    /// panel's "Script Actions" row binds its own <c>IsVisible</c> to, so
    /// the row (and its label) takes no space at all when there's nothing
    /// to show, rather than an empty label with no buttons after it.
    /// </summary>
    public bool HasToolsMenuEntries => ToolsMenuEntries.Count > 0;

    /// <summary>
    /// The session grid's right-click context menu -- one per
    /// <c>ContextAction</c> the currently-loaded script declares. Same
    /// rebuild timing as <see cref="RulesMenuEntries"/>; see
    /// <see cref="InvokeContextAction"/> for what it actually operates on.
    /// </summary>
    public ObservableCollection<ActionMenuEntryViewModel> ContextActionEntries { get; } = [];

    /// <summary>
    /// A live passthrough to <see cref="_fiddlerScriptRunner"/>'s own
    /// <c>Directives</c> -- exists purely so <c>MainWindow.axaml.cs</c> can
    /// read <c>UIColumns</c> to rebuild the session grid's script-provided
    /// columns (see <c>SessionRow.ScriptColumns</c>) from code-behind
    /// without reaching past this view model into
    /// <see cref="_fiddlerScriptRunner"/> directly. Read after
    /// <see cref="FiddlerScriptStatus"/> changes -- that property changing
    /// is the signal a script just (re)loaded, since both are updated
    /// together in <see cref="RefreshFiddlerScriptStatus"/>.
    /// </summary>
    public FiddlerScriptDirectives ScriptDirectives => _fiddlerScriptRunner.Directives;

    /// <summary>
    /// The Request/Response tab content for whichever row is selected in
    /// the grid, recomputed fresh on every selection change (see
    /// <see cref="RefreshInspectors"/>) rather than kept live -- a captured
    /// session's headers and body never change after the fact, so there's
    /// nothing for these to react to beyond "which session is selected now".
    /// </summary>
    public ObservableCollection<InspectorTabViewModel> RequestInspectors { get; } = [];

    public ObservableCollection<InspectorTabViewModel> ResponseInspectors { get; } = [];

    public SessionRow? SelectedSessionRow
    {
        get => _selectedSessionRow;
        set
        {
            if (SetField(ref _selectedSessionRow, value))
            {
                RefreshInspectors();
                RemoveSelectedSessionCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Whether this build can intercept at all. Windows and macOS both
    /// have a real certificate trust-store and system-proxy-registration
    /// path now -- see the Interception Certificate Design doc's "Platform
    /// status" section, including what's still unconfirmed on macOS
    /// specifically without a real Mac to run it against. Anything else
    /// (Linux, in practice, since this app targets plain <c>net10.0</c>
    /// and could technically run there) has neither.
    /// </summary>
    public bool IsSupported { get; } = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    /// <summary>
    /// Nullable decimal to match <c>NumericUpDown.Value</c>'s own type --
    /// binding straight to it needs no converter. Null (the field cleared
    /// while editing) is treated as "use the default" at Start time rather
    /// than blocking the click. Only consulted at all when
    /// <see cref="UseAutomaticPort"/> is false -- see <see cref="Start"/>.
    /// </summary>
    public decimal? PreferredPort
    {
        get => _preferredPort;
        set
        {
            if (SetField(ref _preferredPort, value))
            {
                StartCommand.RaiseCanExecuteChanged();

                // Only a real port is worth remembering -- a cleared field
                // mid-edit (null) leaves the last good value in place.
                if (value is >= 1 and <= 65535)
                {
                    _preferences.SetInt32Pref(PreferenceKeys.Port, (int)value.Value);
                }
            }
        }
    }

    /// <summary>
    /// True for "Auto" (let the OS hand back any free port -- the same
    /// port-0 trick <see cref="InterceptingProxyListener.StartOnAvailablePort"/>
    /// already falls back to when a preferred port is taken, just requested
    /// up front instead of after a collision), false for "Specify" (use
    /// <see cref="PreferredPort"/>). Defaults to true: a first run picks
    /// whatever port the OS hands back rather than assuming 8888 is free.
    /// <see cref="PreferredPort"/> is left populated with its own default
    /// (8888) regardless, so switching to "Specify" still starts from a
    /// sensible value instead of an empty field.
    /// </summary>
    public bool UseAutomaticPort
    {
        get => _useAutomaticPort;
        set
        {
            if (SetField(ref _useAutomaticPort, value))
            {
                RaisePropertyChanged(nameof(UseSpecificPort));
                StartCommand.RaiseCanExecuteChanged();
                _preferences.SetBoolPref(PreferenceKeys.PortAutomatic, value);
            }
        }
    }

    /// <summary>
    /// The exact negation of <see cref="UseAutomaticPort"/>, kept as its
    /// own two-way-bindable property purely so the "Specify" RadioButton in
    /// MainWindow.axaml has something real to bind <c>IsChecked</c> to.
    /// Two RadioButtons in one GroupName only stay correctly in sync (both
    /// on the initial, unclicked render, and afterward) if each one binds
    /// TwoWay to an actual property -- a bare <c>{Binding !UseAutomaticPort}</c>
    /// on the second button would show it unchecked at startup regardless
    /// of <see cref="UseAutomaticPort"/>'s actual value, since nothing
    /// would ever set its IsChecked to true in the first place.
    /// </summary>
    public bool UseSpecificPort
    {
        get => !_useAutomaticPort;
        set => UseAutomaticPort = !value;
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetField(ref _isRunning, value))
            {
                StartCommand.RaiseCanExecuteChanged();
                StopCommand.RaiseCanExecuteChanged();
                RaisePropertyChanged(nameof(ShowSteadyRunningIndicator));
            }
        }
    }

    /// <summary>
    /// True (private setter -- only <see cref="FlashCaptureIndicator"/> and
    /// <see cref="Stop"/> ever change it) for a brief window after each
    /// captured session, so the status bar's dot flashes on new traffic
    /// rather than just sitting steady the whole time the proxy runs. See
    /// <see cref="ShowSteadyRunningIndicator"/> for how this and
    /// <see cref="IsRunning"/> combine into the status bar's three
    /// mutually-exclusive dots (flashing / steady-running / stopped).
    /// </summary>
    public bool IsCaptureFlashing
    {
        get => _isCaptureFlashing;
        private set
        {
            if (SetField(ref _isCaptureFlashing, value))
            {
                RaisePropertyChanged(nameof(ShowSteadyRunningIndicator));
            }
        }
    }

    /// <summary>
    /// The "running, but nothing just came in" dot -- true while the proxy
    /// is running and NOT currently mid-flash. Together with
    /// <see cref="IsCaptureFlashing"/> and <c>!IsRunning</c> (bound directly
    /// in MainWindow.axaml, no property needed for that one) these three
    /// are mutually exclusive and exactly one is ever visible, so the
    /// status bar reads as exactly one dot that flashes brighter when
    /// traffic is actively coming in. This way the status bar reads as
    /// "alive" even during a quiet stretch rather than looking identical to
    /// stopped.
    /// </summary>
    public bool ShowSteadyRunningIndicator => IsRunning && !IsCaptureFlashing;

    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand SaveSazCommand { get; }
    public RelayCommand ImportViaExtensionCommand { get; }
    public RelayCommand ExportViaExtensionCommand { get; }
    public RelayCommand OpenDocumentationCommand { get; }

    /// <summary>Edit > Remove Selected Session (also the Delete key in the session list).</summary>
    public RelayCommand RemoveSelectedSessionCommand { get; }

    /// <summary>Edit > Remove All Sessions.</summary>
    public RelayCommand RemoveAllSessionsCommand { get; }

    /// <param name="confirmMacOSCertificateTrust">
    /// See <see cref="_confirmMacOSCertificateTrust"/>'s own remarks.
    /// Optional (defaults to null, meaning "always decline on macOS") so
    /// every other caller -- tests, and the Windows path, which never
    /// consults this at all -- doesn't need to pass one.
    /// </param>
    /// <param name="preferences">
    /// Where settings are loaded from and saved to. Optional: null means
    /// the real per-user file (see <see cref="CreateDefaultPreferences"/>).
    /// This view model takes ownership either way and disposes it in
    /// <see cref="Dispose"/>.
    /// </param>
    /// <param name="chooseFormat">See <see cref="_chooseFormat"/>. Optional.</param>
    public MainWindowViewModel(
        Func<bool>? confirmMacOSCertificateTrust = null,
        PreferenceStore? preferences = null,
        Func<string, IReadOnlyList<FormatChoice>, string?, Task<FormatChoice?>>? chooseFormat = null)
    {
        _confirmMacOSCertificateTrust = confirmMacOSCertificateTrust;
        _chooseFormat = chooseFormat;

        // Before anything else below reads the fields these populate.
        // Written straight to the backing fields rather than through the
        // property setters, so loading a value never immediately writes it
        // back out again.
        _preferences = preferences ?? CreateDefaultPreferences();
        LoadPreferences();

        // Ported Fiddler-shaped extensions reach preferences and logging
        // through the compatibility layer's host hooks; they share the
        // app's own preferences file (same behavior on Windows and macOS).
        // Set before extensions load below, since OnLoad may read them.
        // The file-picker and notify hooks need the real window, so
        // App.axaml.cs sets those.
        Clearinet.CompatShim.CompatShimHost.Preferences = _preferences;
        Clearinet.CompatShim.CompatShimHost.Log = message => Console.WriteLine($"[Extension] {message}");

        // Must run before anything below touches the system proxy itself:
        // a backup file still sitting on disk means the *previous* run of
        // this app never reached Stop/Dispose (crash, kill, an unclean
        // shutdown) and left the system proxy pointed at a port nothing is
        // listening on any more. See SystemProxyController's own remarks
        // for the full design, on both platforms it dispatches to.
        SystemProxyController.RecoverFromCrash();

        // Loaded once, synchronously, here at startup -- see ExtensionHost's
        // own remarks on why compiled extensions don't get FiddlerScript's
        // live-reload treatment. DefaultExtensionsFolder may well not exist
        // on a machine that has never used extensions; ExtensionHost.Load()
        // treats that as nothing to load, not an error (see its own
        // remarks), so this is safe to call unconditionally on every launch.
        _extensionHost = new ExtensionHost(
            // The user's own folder first, so their copy of an extension
            // wins over an optional one the installer put next to the app.
            [ExtensionHost.DefaultExtensionsFolder, ExtensionHost.BundledExtensionsFolder],
            log: message => Console.WriteLine($"[Extension] {message}"));
        // Extensions add their tabs from OnLoad, inside Load() below.
        ExtensionUi.SetTabHost((title, view) => ExtensionTabs.Add(new ExtensionTabViewModel(title, view)));
        _extensionHost.Load();
        foreach (var loadError in _extensionHost.LoadErrors)
        {
            Console.WriteLine($"[Extension] {loadError}");
        }
        ExtensionStatus = BuildExtensionStatus();

        // Folds any compiled extension inspectors in alongside the three
        // built-ins -- see InspectorRegistry.CreateDefault(IEnumerable<IInspector>)'s
        // own remarks on this being the seam it was written for. Built here,
        // after Load() above, rather than as a field initializer (like the
        // three-built-ins-only version this replaced) since it now depends
        // on what Load() found.
        _inspectorRegistry = InspectorRegistry.CreateDefault(
            _extensionHost.RequestInspectors.Select(i => (IInspector)Inspector2Adapter.ForRequest(i))
                .Concat(_extensionHost.ResponseInspectors.Select(i => (IInspector)Inspector2Adapter.ForResponse(i))));

        StartCommand = new RelayCommand(
            Start, () => IsSupported && !IsRunning && (UseAutomaticPort || PreferredPort is >= 1 and <= 65535));
        StopCommand = new RelayCommand(Stop, () => IsRunning);
        SaveSazCommand = new RelayCommand(SaveSaz, () => Sessions.Count > 0);
        // The Importers/Exporters.Count half of each condition is a one-time
        // check against what Load() already found above -- extensions don't
        // come and go mid-run (see ExtensionHost's own remarks), so that
        // half never needs RaiseCanExecuteChanged. ExportViaExtensionCommand's
        // Sessions.Count > 0 half is exactly SaveSazCommand's own condition,
        // so it rides along on the same RaiseCanExecuteChanged call in the
        // SessionAdded handler below.
        ImportViaExtensionCommand = new RelayCommand(ImportViaExtension, () => _extensionHost.Importers.Count > 0);
        ExportViaExtensionCommand = new RelayCommand(ExportViaExtension, () => _extensionHost.Exporters.Count > 0 && Sessions.Count > 0);
        OpenDocumentationCommand = new RelayCommand(OpenDocumentation);
        RemoveSelectedSessionCommand = new RelayCommand(
            () => { if (SelectedSessionRow is { } row) { _sessionStore.Remove([row.Id]); } },
            () => SelectedSessionRow is not null);
        RemoveAllSessionsCommand = new RelayCommand(() => _sessionStore.Clear(), () => Sessions.Count > 0);

        AddAutoResponderRuleCommand = new RelayCommand(AddAutoResponderRule);
        RemoveAutoResponderRuleCommand = new RelayCommand(RemoveSelectedAutoResponderRule, () => SelectedAutoResponderRule is not null);
        MoveAutoResponderRuleUpCommand = new RelayCommand(() => MoveSelectedAutoResponderRule(-1), () => CanMoveSelectedAutoResponderRule(-1));
        MoveAutoResponderRuleDownCommand = new RelayCommand(() => MoveSelectedAutoResponderRule(1), () => CanMoveSelectedAutoResponderRule(1));

        // The log callback routes FiddlerObject.alert()/Log.LogString()
        // calls (and this runner's own handler-error notices) to the
        // console -- the same place every other diagnostic in this proxy
        // core already goes (see InterceptingProxyListener's own
        // Console.WriteLine calls), rather than a dedicated log pane this
        // pass doesn't build.
        _fiddlerScriptRunner = new FiddlerScriptRunner(log: message => Console.WriteLine($"[FiddlerScript] {message}"));
        LoadFiddlerScriptCommand = new RelayCommand(LoadFiddlerScript, () => !string.IsNullOrWhiteSpace(FiddlerScriptPath));
        ReloadFiddlerScriptCommand = new RelayCommand(ReloadFiddlerScript, () => _fiddlerScriptRunner.IsLoaded);

        // DispatcherTimer over a raw Task.Delay/CancellationTokenSource
        // specifically because Tick always fires on the UI thread -- every
        // other event handler in this constructor already has to marshal
        // through Dispatcher.UIThread.Post for that same reason, and a timer
        // sidesteps needing to do that here too.
        _captureFlashTimer.Tick += (_, _) =>
        {
            _captureFlashTimer.Stop();
            IsCaptureFlashing = false;
        };

        // Registered after the commands above are assigned -- the compiler's
        // nullable flow analysis judges a captured non-nullable property by
        // where its assignment appears in the constructor's text, not by
        // when the closure actually runs, so referencing SaveSazCommand here
        // before its assignment line would (harmlessly, since the closure
        // only ever runs post-construction) still trip CS8602.
        _sessionStore.SessionAdded += session => Dispatcher.UIThread.Post(() =>
        {
            var row = SessionRow.From(session, _fiddlerScriptRunner);
            Sessions.Add(row);
            // Only this one new row needs checking against the current
            // filter -- everything already in FilteredSessions was already
            // matched when it arrived (or by the last ApplyFilter() run),
            // so there's no reason to re-scan the whole list per capture.
            // ui-hide: an importer, extension or script asked for this
            // session to be left out of the list (it's still captured and saved).
            if (!row.Style.Hidden && _query.Matches(session))
            {
                FilteredSessions.Add(row);
            }

            SaveSazCommand.RaiseCanExecuteChanged();
            ExportViaExtensionCommand.RaiseCanExecuteChanged();
            RemoveAllSessionsCommand.RaiseCanExecuteChanged();
            FlashCaptureIndicator();
        });

        // Removed sessions leave both lists; the selection goes with them.
        _sessionStore.SessionsRemoved += ids => Dispatcher.UIThread.Post(() =>
        {
            var removed = new HashSet<int>(ids);
            for (var i = Sessions.Count - 1; i >= 0; i--)
            {
                if (removed.Contains(Sessions[i].Id))
                {
                    Sessions.RemoveAt(i);
                }
            }

            for (var i = FilteredSessions.Count - 1; i >= 0; i--)
            {
                if (removed.Contains(FilteredSessions[i].Id))
                {
                    FilteredSessions.RemoveAt(i);
                }
            }

            if (SelectedSessionRow is { } selected && removed.Contains(selected.Id))
            {
                SelectedSessionRow = null;
            }

            SaveSazCommand.RaiseCanExecuteChanged();
            ExportViaExtensionCommand.RaiseCanExecuteChanged();
            RemoveAllSessionsCommand.RaiseCanExecuteChanged();
        });

        _breakpointManager.BreakpointHit += pending => Dispatcher.UIThread.Post(() =>
        {
            var vm = new PendingBreakpointViewModel(pending);
            _pendingBreakpointViewModels[pending] = vm;
            PendingBreakpoints.Add(vm);
            SelectedPendingBreakpoint ??= vm;
            RaisePropertyChanged(nameof(IsBreakpointsPanelVisible));
        });

        _breakpointManager.BreakpointResolved += pending => Dispatcher.UIThread.Post(() =>
        {
            if (!_pendingBreakpointViewModels.Remove(pending, out var vm))
            {
                return;
            }

            PendingBreakpoints.Remove(vm);
            if (ReferenceEquals(SelectedPendingBreakpoint, vm))
            {
                SelectedPendingBreakpoint = PendingBreakpoints.Count > 0 ? PendingBreakpoints[0] : null;
            }

            RaisePropertyChanged(nameof(IsBreakpointsPanelVisible));
        });

        _statusText = IsSupported
            ? "Not started. Pick a port and click Start."
            : "This build only implements the certificate trust-store path for Windows and macOS so far " +
              "-- see the Interception Certificate Design doc's open risks.";
    }

    /// <summary>
    /// Shows a message from an extension (FiddlerApplication.DoNotifyUser) in
    /// the status line and the console. Deliberately not a modal dialog: an
    /// import can report several warnings in a row, and a stack of dialogs
    /// would be worse than the status line. Must be called on the UI thread
    /// (App.axaml.cs posts it there).
    /// </summary>
    internal void ShowExtensionNotice(string title, string message)
    {
        Console.WriteLine($"[Extension] {title}: {message}");
        StatusText = $"{title}: {message.ReplaceLineEndings(" ")}";
    }

    private static PreferenceStore CreateDefaultPreferences()
    {
        try
        {
            return PreferenceStore.CreateDefault(log: message => Console.WriteLine($"[Preferences] {message}"));
        }
        catch (InvalidOperationException ex)
        {
            // ClearinetPaths found no app-data folder at all. Settings are a
            // convenience: run with in-memory-only preferences rather than
            // not at all.
            Console.WriteLine($"[Preferences] {ex.Message} Settings won't be saved this run.");
            return PreferenceStore.CreateInMemory();
        }
    }

    /// <summary>
    /// Reads every <see cref="PreferenceKeys"/> value into its backing
    /// field. Defaults match what these fields were initialized to before
    /// preferences existed, so a first run (no file yet) looks exactly the
    /// way the app always has.
    /// </summary>
    private void LoadPreferences()
    {
        _useAutomaticPort = _preferences.GetBoolPref(PreferenceKeys.PortAutomatic, true);

        var port = _preferences.GetInt32Pref(PreferenceKeys.Port, DefaultPreferredPort);
        _preferredPort = port is >= 1 and <= 65535 ? port : DefaultPreferredPort;

        _showFiddlerScriptPanel = _preferences.GetBoolPref(PreferenceKeys.ShowFiddlerScriptPanel, false);
        _showExtensionsPanel = _preferences.GetBoolPref(PreferenceKeys.ShowExtensionsPanel, false);
        _showLegacyExtensionHostPanel = _preferences.GetBoolPref(PreferenceKeys.ShowLegacyExtensionHostPanel, false);
        _showAlsoBreakOnRow = _preferences.GetBoolPref(PreferenceKeys.ShowAlsoBreakOnRow, false);

        _filterText = _preferences.GetStringPref(PreferenceKeys.FilterText, string.Empty);
        _query = SessionQuery.Parse(_filterText);

        _fiddlerScriptPath = _preferences.GetStringPref(PreferenceKeys.FiddlerScriptPath, string.Empty);

        _autoLaunchLegacyHost = _preferences.GetBoolPref(PreferenceKeys.AutoLaunchLegacyHost, false);
        if (_autoLaunchLegacyHost)
        {
            _legacyExtensionHostStatus = "Auto-launch is on -- it will launch the next time you click Start.";
        }
    }

    /// <summary>
    /// Shows the port the listener actually bound to (in Auto mode, or when
    /// a specified port was taken) without saving it as the person's
    /// chosen port -- only an edit they make themselves should change
    /// <see cref="PreferenceKeys.Port"/>.
    /// </summary>
    private void ShowBoundPort(int port)
    {
        if (SetField(ref _preferredPort, (decimal?)port, nameof(PreferredPort)))
        {
            StartCommand.RaiseCanExecuteChanged();
        }
    }

    private void Start()
    {
        try
        {
            // Only relevant the very first time this runs on macOS (or
            // again on a later Start click if declined last time --
            // _authority stays null until this is accepted, so this check
            // naturally re-asks). Windows never reaches this branch at
            // all: its own trust-install confirmation is the native OS
            // dialog X509Store.Add triggers, not this one -- see
            // _confirmMacOSCertificateTrust's own remarks and the design
            // doc's "silent-install tension" section.
            if (_authority is null && OperatingSystem.IsMacOS() && _confirmMacOSCertificateTrust?.Invoke() != true)
            {
                StatusText =
                    "Not started -- installing the trusted root certificate was declined. CLeARINET can't " +
                    "decrypt HTTPS traffic without it, so there's no way to proceed without installing it.";
                return;
            }

            // Reused across Start/Stop/Start cycles within one run of the
            // app -- there's no reason to re-install a new trust-store
            // root every time, only a fresh leaf provider (cheap, and it
            // keeps each run's leaves independent of any that came before).
            _authority ??= new CertificateAuthority();
            var leafProvider = new LeafCertificateProvider(_authority.RootCertificate);
            // Port 0 is the same "let the OS pick" signal
            // StartOnAvailablePort itself falls back to on a collision --
            // "Auto" just asks for that straight away instead of trying a
            // preferred port first.
            var requestedPort = UseAutomaticPort ? 0 : (int)(PreferredPort ?? DefaultPreferredPort);

            // Opt-in only -- see AutoLaunchLegacyHost's and
            // LegacyExtensionHostLauncher's own remarks on why this never
            // runs unless the person has explicitly turned it on. Runs
            // before the Probe() call just below (not after) so a
            // freshly-launched process has a real chance of being wired in
            // for *this* Start, not just discovered on the next one --
            // LegacyExtensionHostLauncher.EnsureRunning itself waits (up to
            // a bounded timeout) for the process's pipe to actually come up
            // before returning, specifically so this ordering works.
            if (AutoLaunchLegacyHost)
            {
                var launchResult = _legacyExtensionHostLauncher.EnsureRunning(
                    log: message => Console.WriteLine($"[Extension] {message}"));
                LegacyExtensionHostStatus = launchResult.Message;
            }
            else
            {
                LegacyExtensionHostStatus = "Auto-launch is off -- Tools -> Legacy Extension Host to turn it on.";
            }

            // Probed fresh on every Start() click, deliberately unlike
            // _extensionHost above (loaded once, at app launch, never
            // reloaded) -- see LegacyExtensionHostBridgeClient's own
            // remarks for why: the legacy host .exe is a separate,
            // optional process a person may well launch (or relaunch)
            // *after* opening CLeARINET, and re-probing here means Stop
            // then Start picks that up with no restart of this app needed.
            // Probing never throws and never blocks longer than
            // SessionBridgeProtocol.ConnectTimeoutMilliseconds -- see that
            // class's own remarks -- so this stays safe to call
            // unconditionally whether or not anyone is using the legacy
            // host at all.
            var legacyBridge = LegacyExtensionHostBridgeClient.Probe(
                log: message => Console.WriteLine($"[Extension] {message}"));

            _proxy = InterceptingProxyListener.StartOnAvailablePort(
                requestedPort, leafProvider, _sessionStore, _breakpointManager, _autoResponderRules, _fiddlerScriptRunner,
                new CompositeExtensionAutoTamperHost([_extensionHost.CreateAutoTamperHost(), legacyBridge]),
                _extensionHost.CreateSessionHost());
            IsRunning = true;
            ShowBoundPort(_proxy.Port);

            // In Auto mode requestedPort is always 0, which _proxy.Port
            // (the real bound port) will never equal -- there's no
            // "preferred port was taken" collision to report in that mode
            // in the first place, so this note only ever applies to Specify.
            var portNote = UseAutomaticPort || _proxy.Port == requestedPort
                ? string.Empty
                : $" (port {requestedPort} was already taken -- probably another CLeARINET process still running)";

            // A failure here shouldn't roll back the listener that just
            // started successfully above -- worst case, CLeARINET works
            // exactly like it did before this feature existed (a manually
            // pointed browser proxy), it just doesn't also become the
            // system default.
            try
            {
                SystemProxyController.Enable(_proxy.Port);
                StatusText =
                    $"Listening on 127.0.0.1:{_proxy.Port}{portNote} and registered as the system proxy -- " +
                    "browsers and most other apps on this machine will route through here automatically " +
                    "until you click Stop.";
            }
            catch (Exception ex)
            {
                StatusText =
                    $"Listening on 127.0.0.1:{_proxy.Port}{portNote}, but couldn't register as the system " +
                    $"proxy ({ex.Message}) -- point a browser's HTTPS proxy here manually instead.";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to start the proxy: {ex.Message}";
        }
    }

    private void Stop()
    {
        if (_proxy is null)
        {
            return;
        }

        _proxy.Stop();
        _proxy = null;
        SystemProxyController.Disable();
        IsRunning = false;

        // A no-op unless this app's own Start() is what launched the legacy
        // host in the first place (see LegacyExtensionHostLauncher's own
        // remarks) -- a legacy host reachable because a person started it
        // themselves is left running exactly as it was before this Stop
        // click, matching the "never a hard dependency, never a surprise"
        // posture the rest of this feature already commits to.
        _legacyExtensionHostLauncher.StopIfLaunchedByUs(
            log: message => Console.WriteLine($"[Extension] {message}"));

        // Defensive rather than load-bearing -- SessionAdded can't fire
        // while stopped, so nothing would restart the timer from here on.
        // Still worth guaranteeing no flash state lingers into the next run.
        _captureFlashTimer.Stop();
        IsCaptureFlashing = false;

        StatusText = $"Stopped and released as the system proxy. {Sessions.Count} session(s) captured this run -- " +
                      "Start again to keep going, or File > Save SAZ to export what's here.";
    }

    /// <summary>
    /// Lights the flash dot and (re)starts its fade-out countdown -- called
    /// once per captured session. Restarting an already-running
    /// DispatcherTimer (Stop then Start) rather than letting an in-flight
    /// one run to completion is what gives "stays lit through a burst of
    /// traffic, fades shortly after things go quiet" instead of a blink per
    /// session.
    /// </summary>
    private void FlashCaptureIndicator()
    {
        IsCaptureFlashing = true;
        _captureFlashTimer.Stop();
        _captureFlashTimer.Start();
    }

    /// <summary>
    /// Appends a fresh, disabled, empty rule to the end of the list (both
    /// <see cref="AutoResponderRules.Rules"/> and <see cref="AutoResponderRuleRows"/>)
    /// and selects it -- so clicking "Add" always gives you something to
    /// immediately start typing into, matching Fiddler Classic's own
    /// AutoResponder "Add Rule" behavior. Starts disabled (rather than
    /// inheriting <see cref="AutoResponderEnabled"/>) since an empty
    /// match/action pair matches everything and would otherwise start
    /// intercepting traffic before the person has typed anything into it.
    /// </summary>
    private void AddAutoResponderRule()
    {
        var rule = new AutoResponderRule { IsEnabled = false };
        _autoResponderRules.Rules.Add(rule);
        var vm = new AutoResponderRuleViewModel(rule);
        AutoResponderRuleRows.Add(vm);
        SelectedAutoResponderRule = vm;
    }

    /// <summary>
    /// Reads <see cref="FiddlerScriptPath"/> and loads it into
    /// <see cref="_fiddlerScriptRunner"/> -- Fiddler's own
    /// "Rules > Customize Rules" workflow, minus the text editor itself
    /// (the script is edited in whatever editor the person already uses;
    /// this button just (re)reads it). A file that can't even be read (bad
    /// path, permissions) surfaces here rather than as an unhandled
    /// exception, same posture as <see cref="Start"/>'s own try/catch
    /// around a failed listener start; a file that *is* read but has a
    /// broken script inside it is a different failure
    /// (<see cref="FiddlerScriptRunner.LoadError"/>), surfaced the same way
    /// through <see cref="RefreshFiddlerScriptStatus"/>.
    /// </summary>
    private void LoadFiddlerScript()
    {
        try
        {
            _fiddlerScriptRunner.LoadFromFile(FiddlerScriptPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FiddlerScriptStatus = $"Couldn't read '{FiddlerScriptPath}': {ex.Message}";
            ReloadFiddlerScriptCommand.RaiseCanExecuteChanged();
            return;
        }

        RefreshFiddlerScriptStatus();
        ReloadFiddlerScriptCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Fiddler's own <c>FiddlerObject.ReloadScript()</c> reaches this same
    /// method (see the constructor's <c>reloadScript</c> callback passed
    /// into <see cref="FiddlerScriptRunner"/>) -- a script can trigger its
    /// own reload, not just this button.
    /// </summary>
    private void ReloadFiddlerScript()
    {
        _fiddlerScriptRunner.Reload();
        RefreshFiddlerScriptStatus();
    }

    private void RefreshFiddlerScriptStatus()
    {
        FiddlerScriptStatus = _fiddlerScriptRunner.LoadError is { } error
            ? $"Script error -- still running the last-good script, if any: {error}"
            : $"Loaded '{_fiddlerScriptRunner.LoadedPath}'. " +
              $"OnBeforeRequest: {(_fiddlerScriptRunner.HasOnBeforeRequest ? "yes" : "no")}, " +
              $"OnBeforeResponse: {(_fiddlerScriptRunner.HasOnBeforeResponse ? "yes" : "no")}.";

        // Rules-menu/Tools-menu/Context-menu entries and the grid's
        // script-provided columns are all only ever as fresh as the
        // currently-loaded script -- rebuilding them here, alongside the
        // status line itself, means every call site that already refreshes
        // one (LoadFiddlerScript/ReloadFiddlerScript) gets the other for
        // free, with no separate call to remember.
        RefreshScriptMenus();
    }

    /// <summary>
    /// Rebuilds <see cref="RulesMenuEntries"/>/<see cref="ToolsMenuEntries"/>/
    /// <see cref="ContextActionEntries"/> from scratch against whatever
    /// <see cref="_fiddlerScriptRunner"/>'s <c>Directives</c> currently say
    /// -- called once per script (re)load, from
    /// <see cref="RefreshFiddlerScriptStatus"/>, never incrementally
    /// patched. A full rebuild is simpler to reason about than diffing the
    /// old script's menu against the new one, and a script (re)load is
    /// already a rare, explicit, human-driven action (see
    /// <c>FiddlerScriptRunner</c>'s own remarks on why compiled extensions
    /// and FiddlerScript both treat reload as an occasional event, not a
    /// hot path) -- there's no per-request cost here to worry about.
    /// </summary>
    private void RefreshScriptMenus()
    {
        RulesMenuEntries.Clear();
        foreach (var entry in BuildRulesMenuEntries())
        {
            RulesMenuEntries.Add(entry);
        }

        RaisePropertyChanged(nameof(HasRulesMenuEntries));

        ToolsMenuEntries.Clear();
        foreach (var descriptor in _fiddlerScriptRunner.Directives.ToolsActions)
        {
            ToolsMenuEntries.Add(new ActionMenuEntryViewModel(
                descriptor.MenuText, () => _fiddlerScriptRunner.InvokeToolsAction(descriptor.MethodName)));
        }

        RaisePropertyChanged(nameof(HasToolsMenuEntries));

        ContextActionEntries.Clear();
        foreach (var descriptor in _fiddlerScriptRunner.Directives.ContextActions)
        {
            ContextActionEntries.Add(new ActionMenuEntryViewModel(
                descriptor.MenuText, () => InvokeContextAction(descriptor.MethodName)));
        }
    }

    /// <summary>
    /// Runs a <c>ContextAction</c> against <see cref="SelectedSessionRow"/>
    /// -- a no-op with nothing selected, rather than an error, since the
    /// context menu that offers this is only ever opened from a row the
    /// person right-clicked in the first place, but a click landing between
    /// the menu opening and the selection changing underneath it (a fast
    /// re-click, a session list that just scrolled) is cheap to guard
    /// against here.
    /// </summary>
    private void InvokeContextAction(string methodName)
    {
        if (SelectedSessionRow is { } selected)
        {
            _fiddlerScriptRunner.InvokeContextAction(methodName, [selected.Session]);
        }
    }

    /// <summary>
    /// Builds one <see cref="RulesMenuEntryViewModel"/> per
    /// <c>RulesMenuOption</c> and per <c>RulesStringChoice</c> the loaded
    /// script declares, wiring up each one's checked-state callback to
    /// read/write the underlying script field through
    /// <see cref="_fiddlerScriptRunner"/> and, for a radio-grouped option or
    /// a string-choice submenu, to keep its sibling entries (and their own
    /// underlying fields -- not just their checkmarks) in sync. See
    /// <see cref="RulesMenuOption"/>'s own remarks on what "radio-grouped"
    /// means here, and <c>MainWindow.axaml</c>'s own remarks on why every
    /// entry (grouped or not) renders as one flat list with a
    /// "Submenu: Option" label rather than a real nested flyout.
    /// </summary>
    private List<RulesMenuEntryViewModel> BuildRulesMenuEntries()
    {
        var entries = new List<RulesMenuEntryViewModel>();
        var radioGroups = new Dictionary<string, List<(RulesMenuOption Option, RulesMenuEntryViewModel Entry)>>();

        foreach (var option in _fiddlerScriptRunner.Directives.RulesMenuOptions)
        {
            var header = option.SubmenuName is null ? option.MenuText : $"{option.SubmenuName}: {option.MenuText}";
            var initial = _fiddlerScriptRunner.GetRulesOptionValue(option.FieldName);

            List<(RulesMenuOption Option, RulesMenuEntryViewModel Entry)>? group = null;
            if (option.IsRadio && option.SubmenuName is not null)
            {
                if (!radioGroups.TryGetValue(option.SubmenuName, out group))
                {
                    group = [];
                    radioGroups[option.SubmenuName] = group;
                }
            }

            var entry = new RulesMenuEntryViewModel(header, initial, isChecked =>
            {
                _fiddlerScriptRunner.SetRulesOptionValue(option.FieldName, isChecked);

                // Radio semantics: checking this one clears every sibling's
                // own field, not just its checkmark -- otherwise the
                // script would be left with more than one of the group's
                // booleans true at once, even though the UI shows only one
                // checked.
                if (isChecked && group is not null)
                {
                    foreach (var (siblingOption, siblingEntry) in group)
                    {
                        if (!string.Equals(siblingOption.FieldName, option.FieldName, StringComparison.Ordinal))
                        {
                            _fiddlerScriptRunner.SetRulesOptionValue(siblingOption.FieldName, false);
                            siblingEntry.SetCheckedWithoutNotifying(false);
                        }
                    }
                }
            });

            entries.Add(entry);
            group?.Add((option, entry));
        }

        foreach (var stringOption in _fiddlerScriptRunner.Directives.RulesMenuStringOptions)
        {
            var currentValue = _fiddlerScriptRunner.GetRulesStringValue(stringOption.FieldName);
            var group = new List<RulesMenuEntryViewModel>();

            foreach (var choice in stringOption.Choices)
            {
                var header = $"{stringOption.SubmenuName}: {choice.Name}";
                var initial = string.Equals(currentValue, choice.Value, StringComparison.Ordinal);

                RulesMenuEntryViewModel? entry = null;
                entry = new RulesMenuEntryViewModel(header, initial, isChecked =>
                {
                    if (!isChecked)
                    {
                        // Unchecking a radio-style choice directly isn't a
                        // real action in this model -- picking a DIFFERENT
                        // choice is what changes the value. Put this one
                        // back rather than leaving the whole group
                        // unchecked.
                        entry!.SetCheckedWithoutNotifying(true);
                        return;
                    }

                    _fiddlerScriptRunner.SetRulesStringValue(stringOption.FieldName, choice.Value);
                    foreach (var sibling in group)
                    {
                        if (!ReferenceEquals(sibling, entry))
                        {
                            sibling.SetCheckedWithoutNotifying(false);
                        }
                    }
                });

                entries.Add(entry);
                group.Add(entry);
            }
        }

        return entries;
    }

    /// <summary>
    /// Builds <see cref="ExtensionStatus"/> from what <see cref="ExtensionHost.Load"/>
    /// found -- called exactly once, in the constructor, right after
    /// <c>_extensionHost.Load()</c> runs. Deliberately verbose/explicit
    /// (spells out every scanned folder, not just a summary count) since
    /// this exists specifically to answer "did it look in the right place,
    /// and what did it find there" without needing console access at all --
    /// see <see cref="ExtensionStatus"/>'s own remarks.
    /// </summary>
    private string BuildExtensionStatus()
    {
        var lines = new List<string>();

        foreach (var (folder, existed, dllFilesFound) in _extensionHost.ScanResults)
        {
            lines.Add(existed
                ? $"Scanned '{folder}': found {dllFilesFound} .dll file(s)."
                : $"Folder not found, nothing scanned: '{folder}'.");
        }

        var loadedCount = _extensionHost.AutoTampers.Count
            + _extensionHost.RequestInspectors.Count
            + _extensionHost.ResponseInspectors.Count
            + _extensionHost.Importers.Count
            + _extensionHost.Exporters.Count
            + _extensionHost.ExecActionHandlers.Count
            + _extensionHost.ShimExtensions.Count;
        lines.Add(
            $"Loaded: {_extensionHost.AutoTampers.Count} AutoTamper, " +
            $"{_extensionHost.RequestInspectors.Count} request inspector, " +
            $"{_extensionHost.ResponseInspectors.Count} response inspector, " +
            $"{_extensionHost.Importers.Count} importer, " +
            $"{_extensionHost.Exporters.Count} exporter, " +
            $"{_extensionHost.ExecActionHandlers.Count} exec-action handler, " +
            $"{_extensionHost.ShimExtensions.Count} ported Fiddler extension " +
            $"(total {loadedCount}).");

        foreach (var loadError in _extensionHost.LoadErrors)
        {
            lines.Add($"Error: {loadError}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void RemoveSelectedAutoResponderRule()
    {
        var selected = SelectedAutoResponderRule;
        if (selected is null)
        {
            return;
        }

        var index = AutoResponderRuleRows.IndexOf(selected);
        _autoResponderRules.Rules.Remove(selected.Rule);
        AutoResponderRuleRows.Remove(selected);

        SelectedAutoResponderRule = AutoResponderRuleRows.Count == 0
            ? null
            : AutoResponderRuleRows[Math.Min(index, AutoResponderRuleRows.Count - 1)];
    }

    private bool CanMoveSelectedAutoResponderRule(int direction)
    {
        if (SelectedAutoResponderRule is null)
        {
            return false;
        }

        var index = AutoResponderRuleRows.IndexOf(SelectedAutoResponderRule);
        var newIndex = index + direction;
        return newIndex >= 0 && newIndex < AutoResponderRuleRows.Count;
    }

    /// <summary>
    /// Reorders both <see cref="AutoResponderRuleRows"/> (via
    /// <see cref="ObservableCollection{T}.Move"/>, so the ListBox reorders
    /// in place without losing its selection) and the underlying
    /// <see cref="AutoResponderRules.Rules"/> list -- the two have to move
    /// together since <see cref="AutoResponderRules.Evaluate"/> walks its
    /// own list in order, oblivious to whatever order the UI happens to
    /// display rows in.
    /// </summary>
    private void MoveSelectedAutoResponderRule(int direction)
    {
        if (SelectedAutoResponderRule is not { } selected || !CanMoveSelectedAutoResponderRule(direction))
        {
            return;
        }

        var index = AutoResponderRuleRows.IndexOf(selected);
        var newIndex = index + direction;

        AutoResponderRuleRows.Move(index, newIndex);
        _autoResponderRules.Rules.RemoveAt(index);
        _autoResponderRules.Rules.Insert(newIndex, selected.Rule);

        MoveAutoResponderRuleUpCommand.RaiseCanExecuteChanged();
        MoveAutoResponderRuleDownCommand.RaiseCanExecuteChanged();
    }

    private void SaveSaz()
    {
        var sessions = _sessionStore.Snapshot();
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"clearinet-capture-{DateTime.Now:yyyyMMdd-HHmmss}.saz");
            SazWriter.Write(path, sessions);
            StatusText = $"Saved {sessions.Count} session(s) to {path}";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to save SAZ: {ex.Message}";
        }
    }

    /// <summary>
    /// Imports every session from the .saz file at <paramref name="path"/>
    /// into the live session list. <see cref="SazReader"/> adds each one
    /// through <see cref="SessionStore.Add"/>, the same call live capture
    /// makes, so an imported session arrives through the exact same
    /// SessionAdded handler wired up in the constructor -- no separate
    /// "imported row" rendering path to keep in sync with the real one.
    ///
    /// Genuinely async, awaited from an <c>async void</c> click handler in
    /// MainWindow.axaml.cs, not wrapped in a <see cref="RelayCommand"/> --
    /// this app's <c>RelayCommand</c> is synchronous-only, and blocking the
    /// UI thread on this instead (<c>.GetAwaiter().GetResult()</c>) would
    /// risk deadlocking against <see cref="SazReader"/>'s own async reads,
    /// which don't use <c>ConfigureAwait(false)</c> -- see that class's own
    /// remarks.
    /// </summary>
    public async Task ImportSazAsync(string path)
    {
        try
        {
            var result = await SazReader.ImportAsync(path, _sessionStore);
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

    /// <summary>
    /// Rebuilds <see cref="FilteredSessions"/> from scratch against
    /// <see cref="Sessions"/> and the current <see cref="_query"/> -- the
    /// only time a full rescan is warranted, since it's the query that
    /// changed, not any one session (the SessionAdded handler above covers
    /// that far cheaper, one row at a time).
    /// </summary>
    private void ApplyFilter()
    {
        FilteredSessions.Clear();
        foreach (var row in Sessions)
        {
            if (!row.Style.Hidden && _query.Matches(row.Session))
            {
                FilteredSessions.Add(row);
            }
        }
    }

    /// <summary>
    /// Rebuilds both tab lists from scratch for whatever's now selected
    /// (or empties them when nothing is). Runs every applicable inspector
    /// eagerly rather than lazily per-tab -- these are small, local
    /// computations over one session's headers/body, not worth the extra
    /// state a lazy scheme would need.
    /// </summary>
    private void RefreshInspectors()
    {
        RequestInspectors.Clear();
        ResponseInspectors.Clear();

        var session = _selectedSessionRow?.Session;
        if (session is null)
        {
            return;
        }

        AddInspectorTabs(RequestInspectors, new InspectorContext(session, InspectorSide.Request));
        AddInspectorTabs(ResponseInspectors, new InspectorContext(session, InspectorSide.Response));
    }

    private void AddInspectorTabs(ObservableCollection<InspectorTabViewModel> target, InspectorContext context)
    {
        foreach (var inspector in _inspectorRegistry.GetApplicable(context))
        {
            InspectorContent content;
            try
            {
                content = inspector.Inspect(context);
            }
            catch (Exception ex)
            {
                // An inspector throwing shouldn't take the rest of the tabs
                // (or the app) down with it -- show the failure in its own
                // tab instead, same as a well-behaved inspector returning
                // ErrorContent on purpose.
                content = new ErrorContent($"{inspector.DisplayName} inspector failed: {ex.Message}");
            }

            target.Add(new InspectorTabViewModel(inspector.DisplayName, content));
        }
    }

    public void Dispose()
    {
        // Unloaded regardless of whether the proxy is currently running --
        // an extension that never got started still had OnLoad() called
        // above and deserves its OnBeforeUnload() call, matching
        // IFiddlerExtension's own paired-lifecycle contract.
        _extensionHost.Unload();
        ExtensionUi.SetTabHost(null);

        // Also regardless of whether the proxy is currently running --
        // unlike the _proxy.Stop() block just below, this can't simply be
        // reached by falling through the early-return, because EnsureRunning
        // (see Start()) can succeed even in the rare case where the proxy
        // listener itself then fails to start, which would otherwise leave
        // an orphaned legacy host process behind if the app closes (rather
        // than Stop being clicked first) while _proxy never got set. A
        // no-op either way unless this app's own Start() actually launched
        // something -- see LegacyExtensionHostLauncher's own remarks.
        _legacyExtensionHostLauncher.StopIfLaunchedByUs(
            log: message => Console.WriteLine($"[Extension] {message}"));

        // Saved after extensions unload (an extension may store its own
        // settings in OnBeforeUnload, through FiddlerApplication.Prefs), but
        // before the early return below, so settings are saved whether or
        // not the proxy was ever started.
        _preferences.Dispose();

        if (_proxy is null)
        {
            return;
        }

        _proxy.Stop();
        _proxy = null;
        SystemProxyController.Disable();
    }

    /// <summary>
    /// File -&gt; Import via Extension: asks which format to use (see
    /// <see cref="PickFormatAsync"/>), then runs that importer's
    /// <see cref="ISessionImporter.ImportSessions"/> on a background thread
    /// and adds whatever it returns through <see cref="SessionStore.Add"/> --
    /// the same call live capture and <see cref="ImportSazAsync"/> both
    /// already make, so an extension-imported session arrives through the
    /// exact same SessionAdded handler.
    ///
    /// The options dictionary is always empty: the host has no notion of
    /// what any given format needs, so an importer that needs a file path
    /// asks for it itself, as a real Fiddler extension would (a ported one
    /// through Utilities.ObtainOpenFilename, which App.axaml.cs routes to
    /// Avalonia's file picker).
    ///
    /// <c>async void</c> because it's a command handler; everything inside
    /// is caught, so nothing can escape onto the UI thread.
    /// </summary>
    private async void ImportViaExtension()
    {
        try
        {
            var choice = await PickFormatAsync(
                ProfferedFormats.ChoicesFrom(_extensionHost.Importers),
                PreferenceKeys.LastImportFormat,
                "Import sessions using which format?");
            if (choice?.Handler is not ISessionImporter importer)
            {
                return;
            }

            var formatName = choice.Format.Name;
            StatusText = $"Importing via {choice.DisplayName}...";
            var imported = await Task.Run(() => importer.ImportSessions(formatName, new Dictionary<string, object>(), progress: null));

            foreach (var session in imported)
            {
                _sessionStore.Add(session.Host, session.StartedAt, session.Request, session.Response, session.Flags);
            }

            StatusText = $"Imported {imported.Count} session(s) via {choice.DisplayName}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Extension import failed: {ex.Message}";
        }
    }

    /// <summary>
    /// File -&gt; Export via Extension: the export counterpart of
    /// <see cref="ImportViaExtension"/> -- pick a format, then run that
    /// exporter against every captured session on a background thread.
    /// <c>options</c> is empty for the same reason (an exporter that needs a
    /// destination path asks for it itself).
    /// </summary>
    private async void ExportViaExtension()
    {
        try
        {
            var choice = await PickFormatAsync(
                ProfferedFormats.ChoicesFrom(_extensionHost.Exporters),
                PreferenceKeys.LastExportFormat,
                "Export sessions using which format?");
            if (choice?.Handler is not ISessionExporter exporter)
            {
                return;
            }

            var sessions = _sessionStore.Snapshot();
            var formatName = choice.Format.Name;
            StatusText = $"Exporting via {choice.DisplayName}...";
            var succeeded = await Task.Run(() => exporter.ExportSessions(formatName, sessions, new Dictionary<string, object>(), progress: null));

            StatusText = succeeded
                ? $"Exported {sessions.Count} session(s) via {choice.DisplayName}."
                : "Extension export reported failure -- see the console for anything it logged.";
        }
        catch (Exception ex)
        {
            StatusText = $"Extension export failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Which format to import or export with. No dialog when there's only
    /// one choice. With several, the picker supplied by App.axaml.cs
    /// (FormatPickerWindow) is shown with the last choice highlighted, and
    /// the new choice is remembered in preferences (same file and behavior
    /// on Windows and macOS). Null when there are no choices or the user
    /// cancels. A host that supplied no picker (tests, tooling) gets the
    /// remembered or first choice.
    /// </summary>
    private async Task<FormatChoice?> PickFormatAsync(IReadOnlyList<FormatChoice> choices, string preferenceKey, string heading)
    {
        if (choices.Count <= 1)
        {
            return choices.Count == 1 ? choices[0] : null;
        }

        var remembered = _preferences.GetStringPref(preferenceKey, string.Empty);
        if (_chooseFormat is null)
        {
            return ProfferedFormats.Preselect(choices, remembered);
        }

        var chosen = await _chooseFormat(heading, choices, remembered);
        if (chosen is not null)
        {
            _preferences.SetStringPref(preferenceKey, chosen.Key);
        }

        return chosen;
    }

    /// <summary>
    /// Help -&gt; Documentation: opens the bundled end-user-facing User Guide
    /// (docs/User Guide.md in the repo, shipped as loose build output next
    /// to the executable -- see Clearinet.DesktopUi.csproj's own remarks on
    /// the None/Link/CopyToOutputDirectory entry that puts it there) through
    /// whatever the OS has registered for .md files -- the same
    /// UseShellExecute=true approach used to reach a real file-open dialog
    /// nowhere else in this app, since this is the first place that opens a
    /// file rather than picking one. Deliberately a local bundled copy
    /// rather than a GitHub link: as of this pass the repo itself isn't
    /// pushed anywhere with a public URL to point at, and even once it is, a
    /// local copy works offline and always matches whatever build is
    /// actually running, which a hardcoded URL to one specific tag or branch
    /// wouldn't. No live UI control is needed here (unlike
    /// BrowseFiddlerScriptButton_Click/OpenSazMenuItem_Click, which need
    /// TopLevel.StorageProvider), so this lives here rather than in
    /// MainWindow.axaml.cs. A missing association for .md files, or the doc
    /// not having been copied to the output folder for some reason, fails
    /// the same way Start's own try/catch does -- reported through
    /// StatusText rather than an unhandled exception.
    /// </summary>
    private void OpenDocumentation()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Documentation", "User Guide.md");
            if (!File.Exists(path))
            {
                // In the macOS app bundle, documentation lives in
                // Contents/Resources, next to Contents/MacOS: only code may go
                // in Contents/MacOS, or the bundle's signature won't verify.
                var bundled = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Resources", "Documentation", "User Guide.md"));
                if (File.Exists(bundled))
                {
                    path = bundled;
                }
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = $"Couldn't open the documentation: {ex.Message}";
        }
    }
}
