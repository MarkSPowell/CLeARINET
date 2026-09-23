using System.Collections.ObjectModel;
using Avalonia.Threading;
using Clearinet.DesktopUi.Models;
using Clearinet.Extensibility.Inspection;
using Clearinet.ProxyCore.AutoResponder;
using Clearinet.ProxyCore.Breakpoints;
using Clearinet.ProxyCore.Certificates;
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
    private readonly InspectorRegistry _inspectorRegistry = InspectorRegistry.CreateDefault();
    private readonly BreakpointManager _breakpointManager = new();
    private readonly Dictionary<PendingBreakpoint, PendingBreakpointViewModel> _pendingBreakpointViewModels = [];
    private readonly AutoResponderRules _autoResponderRules = new();
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
    private bool _useAutomaticPort;
    private bool _isCaptureFlashing;

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
            }
        }
    }

    /// <summary>
    /// Whether this build can intercept at all. Only the Windows
    /// certificate trust-store path is implemented so far -- see the
    /// Interception Certificate Design doc's open risks for the macOS gap.
    /// </summary>
    public bool IsSupported { get; } = OperatingSystem.IsWindows();

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
            }
        }
    }

    /// <summary>
    /// True for "Auto" (let the OS hand back any free port -- the same
    /// port-0 trick <see cref="InterceptingProxyListener.StartOnAvailablePort"/>
    /// already falls back to when a preferred port is taken, just requested
    /// up front instead of after a collision), false for "Specify" (use
    /// <see cref="PreferredPort"/>). Defaults to false so a first run keeps
    /// behaving exactly like before this toggle existed -- port 8888 unless
    /// changed.
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

    public MainWindowViewModel()
    {
        // Must run before anything below touches the registry itself: a
        // backup file still sitting on disk means the *previous* run of
        // this app never reached Stop/Dispose (crash, kill, an unclean
        // Windows shutdown) and left the system proxy pointed at a port
        // nothing is listening on any more. See WinInetSystemProxy's own
        // remarks for the full design.
        WinInetSystemProxy.RecoverFromCrash();

        StartCommand = new RelayCommand(
            Start, () => IsSupported && !IsRunning && (UseAutomaticPort || PreferredPort is >= 1 and <= 65535));
        StopCommand = new RelayCommand(Stop, () => IsRunning);
        SaveSazCommand = new RelayCommand(SaveSaz, () => Sessions.Count > 0);

        AddAutoResponderRuleCommand = new RelayCommand(AddAutoResponderRule);
        RemoveAutoResponderRuleCommand = new RelayCommand(RemoveSelectedAutoResponderRule, () => SelectedAutoResponderRule is not null);
        MoveAutoResponderRuleUpCommand = new RelayCommand(() => MoveSelectedAutoResponderRule(-1), () => CanMoveSelectedAutoResponderRule(-1));
        MoveAutoResponderRuleDownCommand = new RelayCommand(() => MoveSelectedAutoResponderRule(1), () => CanMoveSelectedAutoResponderRule(1));

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
            var row = SessionRow.From(session);
            Sessions.Add(row);
            // Only this one new row needs checking against the current
            // filter -- everything already in FilteredSessions was already
            // matched when it arrived (or by the last ApplyFilter() run),
            // so there's no reason to re-scan the whole list per capture.
            if (_query.Matches(session))
            {
                FilteredSessions.Add(row);
            }

            SaveSazCommand.RaiseCanExecuteChanged();
            FlashCaptureIndicator();
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
            : "This build only implements the Windows certificate trust-store path so far " +
              "-- see the Interception Certificate Design doc's open risks.";
    }

    private void Start()
    {
        try
        {
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

            _proxy = InterceptingProxyListener.StartOnAvailablePort(
                requestedPort, leafProvider, _sessionStore, _breakpointManager, _autoResponderRules);
            IsRunning = true;
            PreferredPort = _proxy.Port;

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
                WinInetSystemProxy.Enable(_proxy.Port);
                StatusText =
                    $"Listening on 127.0.0.1:{_proxy.Port}{portNote} and registered as the Windows system proxy -- " +
                    "browsers and most other WinINET-aware apps on this machine will route through here " +
                    "automatically until you click Stop.";
            }
            catch (Exception ex)
            {
                StatusText =
                    $"Listening on 127.0.0.1:{_proxy.Port}{portNote}, but couldn't register as the Windows system " +
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
        WinInetSystemProxy.Disable();
        IsRunning = false;

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
            if (_query.Matches(row.Session))
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
        if (_proxy is null)
        {
            return;
        }

        _proxy.Stop();
        _proxy = null;
        WinInetSystemProxy.Disable();
    }
}
