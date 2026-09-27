using System.Diagnostics;

namespace Clearinet.Compatibility.Extensions;

/// <summary>
/// Optionally launches and stops <c>Clearinet.LegacyExtensionHost.exe</c>
/// from the main app itself -- closing the one gap the session bridge's own
/// design doc entry ("The legacy host's session bridge") flagged as "still
/// not built": until now, both processes simply had to find each other over
/// the named pipe if a person happened to already have the legacy host
/// running; nothing here ever started it. See
/// <see cref="LegacyExtensionHostBridgeClient"/> for the other half (talking
/// to it once it's up).
///
/// <b>Opt-in, not automatic.</b> A person who has never touched legacy
/// extensions shouldn't have a second, Windows-only net48 process silently
/// spawned under them the first time they click Start -- see
/// <c>MainWindowViewModel.AutoLaunchLegacyHost</c> (default
/// <see langword="false"/>), which gates every call into
/// <see cref="EnsureRunning"/>. This mirrors the "never a hard dependency"
/// posture <see cref="LegacyExtensionHostBridgeClient"/> already commits to,
/// one level up: not just "safe if it isn't running," but "won't even try
/// to make it exist unless asked."
///
/// <b>Path convention.</b> Mark's own call on how this should be discovered:
/// not a user-configurable setting, and not a hardcoded dev-tree-relative
/// path -- a fixed subdirectory of wherever the main app's own executable
/// lives (<see cref="AppContext.BaseDirectory"/>), matching a real per-user
/// install (main app under
/// <c>%LocalAppData%\Programs\CLeARINET\</c>, legacy host under
/// <c>%LocalAppData%\Programs\CLeARINET\LegacyHost\</c>). <b>This is the
/// installed-release layout, not yet the dev-checkout one</b> -- running the
/// main app via <c>dotnet run</c> from this repo gives an
/// <see cref="AppContext.BaseDirectory"/> under
/// <c>apps/Clearinet.DesktopUi/bin/...</c>, which has no <c>LegacyHost</c>
/// subfolder unless someone copies the built
/// <c>tools/Clearinet.LegacyExtensionHost/Clearinet.LegacyExtensionHost/bin/...</c>
/// output there by hand -- unless the build tooling does that copy for
/// you. It now does, on both sides: an MSBuild post-build copy step for
/// local dev builds (this project's own
/// <c>CopyOutputToDesktopUiLegacyHostFolder</c> target), and an optional,
/// unchecked-by-default Inno Setup Task for a real installed release
/// (<c>installer/CLeARINET.iss</c>'s own <c>legacyhost</c> Task) -- see
/// this feature's own design doc entry for both.
/// </summary>
public sealed class LegacyExtensionHostLauncher
{
    private const string LegacyHostSubdirectoryName = "LegacyHost";
    private const string LegacyHostExecutableName = "Clearinet.LegacyExtensionHost.exe";

    /// <summary>
    /// How long <see cref="EnsureRunning"/> will wait, after a successful
    /// <see cref="Process.Start(ProcessStartInfo)"/>, for the freshly-launched
    /// process's own session bridge pipe to actually come up (WinForms
    /// startup, then <c>SessionBridgeServer</c>'s own listener threads
    /// spinning up) before giving up and returning anyway. Without this,
    /// the caller's very next <see cref="LegacyExtensionHostBridgeClient.Probe"/>
    /// call -- <c>MainWindowViewModel.Start</c> makes it immediately
    /// afterward -- would very likely still see nothing listening yet and
    /// fail open for that entire run, defeating the point of launching it
    /// automatically in the first place (a person would need to click Stop
    /// then Start again before it actually got used). Bounded, not
    /// unbounded: a legacy host that never comes up (a broken build, a
    /// crash on startup) shouldn't hang <see cref="EnsureRunning"/> forever.
    /// </summary>
    private const int LaunchReadyTimeoutMilliseconds = 5000;

    private const int LaunchReadyPollIntervalMilliseconds = 250;

    /// <summary>
    /// Every outcome <see cref="EnsureRunning"/> can report -- kept as a
    /// small closed set rather than a bare bool/exception so
    /// <c>MainWindowViewModel</c> can turn each one into a specific status
    /// line (see <see cref="Result"/>) without re-deriving what happened
    /// from a log string.
    /// </summary>
    public enum Outcome
    {
        /// <summary>
        /// A legacy host was already reachable on the session bridge pipe
        /// (see <see cref="LegacyExtensionHostBridgeClient.Ping"/>) before
        /// this call did anything -- nothing launched, and
        /// <see cref="StopIfLaunchedByUs"/> will later leave it running,
        /// since this app never owned its lifecycle in the first place.
        /// </summary>
        AlreadyRunning,

        /// <summary>
        /// Nothing was reachable, <see cref="ExpectedExecutablePath"/>
        /// existed, and <see cref="Process.Start(ProcessStartInfo)"/>
        /// returned successfully. The process object is retained
        /// internally so a later <see cref="StopIfLaunchedByUs"/> call can
        /// close specifically this one.
        /// </summary>
        Launched,

        /// <summary>
        /// Nothing was reachable, and no file exists at
        /// <see cref="ExpectedExecutablePath"/> -- see this class's own
        /// remarks on the dev-checkout-vs-installed-layout gap, the most
        /// likely real-world cause today.
        /// </summary>
        NotFound,

        /// <summary>
        /// A file exists at <see cref="ExpectedExecutablePath"/>, but
        /// starting it threw (permissions, a corrupt/blocked binary, and so
        /// on) -- the specific exception message is folded into
        /// <see cref="Result.Message"/>, not swallowed.
        /// </summary>
        LaunchFailed,
    }

    /// <summary>
    /// What <see cref="EnsureRunning"/> actually did, plus a ready-to-display
    /// explanation -- <c>MainWindowViewModel.LegacyExtensionHostStatus</c>'s
    /// only source, so that panel never needs its own copy of this
    /// decision's reasoning.
    /// </summary>
    public readonly record struct Result(Outcome Outcome, string Message);

    /// <summary>
    /// Set only by a successful <see cref="EnsureRunning"/> launch, cleared
    /// by <see cref="StopIfLaunchedByUs"/> either way (success or failure) --
    /// the single piece of state this class carries between calls, and the
    /// only thing that makes <see cref="StopIfLaunchedByUs"/> ever act.
    /// Deliberately never set from the <see cref="Outcome.AlreadyRunning"/>
    /// path: a process this app didn't start is a process this app has no
    /// business stopping.
    /// </summary>
    private Process? _launchedProcess;

    /// <summary>
    /// <c>&lt;main app's own executable directory&gt;\LegacyHost\Clearinet.LegacyExtensionHost.exe</c>
    /// -- see this class's own remarks for the reasoning and the current
    /// dev-checkout gap.
    /// </summary>
    public static string ExpectedExecutablePath =>
        Path.Combine(AppContext.BaseDirectory, LegacyHostSubdirectoryName, LegacyHostExecutableName);

    /// <summary>
    /// Pings first (see <see cref="LegacyExtensionHostBridgeClient.Ping"/>)
    /// so Start/Stop/Start within one run, or a person who separately
    /// launched the legacy host by hand, never results in a second instance
    /// racing the first one for the same named pipe (see
    /// <c>SessionBridgeServer</c>'s own remarks on why that's a real
    /// problem, not just wasteful). Never throws -- every failure path
    /// becomes a <see cref="Outcome.NotFound"/>/<see cref="Outcome.LaunchFailed"/>
    /// result instead, so <c>MainWindowViewModel.Start</c> can call this
    /// unconditionally (when the opt-in checkbox is on) with no try/catch of
    /// its own, matching <see cref="LegacyExtensionHostBridgeClient.Probe"/>'s
    /// own never-throws posture one step earlier in the same call chain.
    /// </summary>
    public Result EnsureRunning(Action<string>? log = null)
    {
        log ??= _ => { };

        if (LegacyExtensionHostBridgeClient.Ping(log))
        {
            const string message = "Already running -- using the existing instance.";
            log($"[LegacyExtensionHost] {message}");
            return new Result(Outcome.AlreadyRunning, message);
        }

        var path = ExpectedExecutablePath;
        if (!File.Exists(path))
        {
            var message = $"Not found at '{path}'.";
            log($"[LegacyExtensionHost] {message}");
            return new Result(Outcome.NotFound, message);
        }

        try
        {
            // UseShellExecute: false -- this is a specific, known executable
            // path this app is choosing to run, not a document/URL handed to
            // the OS shell to interpret; ProcessStartInfo's own docs call out
            // ShellExecute as the wrong choice for exactly that case.
            // WorkingDirectory set to the exe's own folder on general
            // principle (nothing in Clearinet.LegacyExtensionHost currently
            // depends on it -- LegacyExtensionLoader resolves its extensions
            // folder from an absolute %USERPROFILE% path -- but an inherited
            // working directory from wherever the main app happens to have
            // been launched from is never the right default for a child
            // process).
            var startInfo = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty,
            };

            _launchedProcess = Process.Start(startInfo);
            log($"[LegacyExtensionHost] Launched '{path}' -- waiting for its session bridge to come up...");

            // See LaunchReadyTimeoutMilliseconds's own remarks for why this
            // wait exists at all. Deliberately a plain blocking poll loop,
            // not async -- MainWindowViewModel.Start is itself fully
            // synchronous (see its own remarks: certificate authority setup,
            // the listener, WinINET registration are all synchronous calls
            // already), so there's no async context here to hand this off
            // to; a future pass could make Start (and this) async if that
            // ever becomes worth the larger change.
            var deadline = Environment.TickCount64 + LaunchReadyTimeoutMilliseconds;
            while (Environment.TickCount64 < deadline)
            {
                if (LegacyExtensionHostBridgeClient.Ping(log))
                {
                    var readyMessage = $"Launched '{path}' -- session bridge is up.";
                    log($"[LegacyExtensionHost] {readyMessage}");
                    return new Result(Outcome.Launched, readyMessage);
                }

                Thread.Sleep(LaunchReadyPollIntervalMilliseconds);
            }

            var timeoutMessage = $"Launched '{path}', but its session bridge wasn't reachable within " +
                                  $"{LaunchReadyTimeoutMilliseconds / 1000}s -- it may still be starting up; " +
                                  "try Stop then Start again in a moment.";
            log($"[LegacyExtensionHost] {timeoutMessage}");
            return new Result(Outcome.Launched, timeoutMessage);
        }
        catch (Exception ex)
        {
            var message = $"Found '{path}' but couldn't launch it: {ex.Message}";
            log($"[LegacyExtensionHost] {message}");
            return new Result(Outcome.LaunchFailed, message);
        }
    }

    /// <summary>
    /// A no-op unless this launcher's own <see cref="EnsureRunning"/> is
    /// what started the process still tracked in <see cref="_launchedProcess"/>
    /// -- a legacy host reachable because someone else started it (including
    /// an earlier <see cref="Outcome.AlreadyRunning"/> result from this same
    /// launcher) is never touched here. Tries
    /// <see cref="Process.CloseMainWindow"/> first -- a real WM_CLOSE to
    /// <c>frmViewer</c>, the same message its own window's X button would
    /// send, giving every loaded legacy extension's <c>OnBeforeUnload</c> a
    /// chance to run through the host's normal FormClosing handling -- and
    /// only escalates to <see cref="Process.Kill()"/> if the process hasn't
    /// exited within a short grace window. Safe to call even if nothing was
    /// ever launched, or if the tracked process already exited on its own
    /// (a person closing the legacy host's window manually) -- never
    /// throws, matching every other method in this pairing's own
    /// never-throws posture.
    /// </summary>
    public void StopIfLaunchedByUs(Action<string>? log = null)
    {
        log ??= _ => { };
        var process = _launchedProcess;
        _launchedProcess = null;

        if (process is null)
        {
            return;
        }

        try
        {
            if (process.HasExited)
            {
                return;
            }

            process.CloseMainWindow();
            if (!process.WaitForExit(2000))
            {
                process.Kill();
            }

            log("[LegacyExtensionHost] Stopped the legacy host process this app launched.");
        }
        catch (Exception ex)
        {
            log($"[LegacyExtensionHost] Couldn't cleanly stop the legacy host process: {ex.Message}");
        }
        finally
        {
            process.Dispose();
        }
    }
}
