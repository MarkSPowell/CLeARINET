using System.Diagnostics;

namespace Violens.Compatibility.Extensions;

/// <summary>
/// Starts and stops <c>Violens.LegacyExtensionHost.exe</c> from the app,
/// only when the user has turned that on. The host is expected in a
/// <c>LegacyHost</c> folder beside the app; the build copies it there for
/// dev builds, and the installer can include it.
/// </summary>
public sealed class LegacyExtensionHostLauncher
{
    private const string LegacyHostSubdirectoryName = "LegacyHost";
    private const string LegacyHostExecutableName = "Violens.LegacyExtensionHost.exe";

    /// <summary>
    /// How long to wait for a newly started host's pipe, so the Probe that
    /// follows finds it.
    /// </summary>
    private const int LaunchReadyTimeoutMilliseconds = 5000;

    private const int LaunchReadyPollIntervalMilliseconds = 250;

    /// <summary>What <see cref="EnsureRunning"/> did.</summary>
    public enum Outcome
    {
        /// <summary>A host was already running; it's left alone on Stop.</summary>
        AlreadyRunning,

        /// <summary>Started; <see cref="StopIfLaunchedByUs"/> will close it.</summary>
        Launched,

        /// <summary>Nothing at <see cref="ExpectedExecutablePath"/>.</summary>
        NotFound,

        /// <summary>The file exists but couldn't be started; the message says why.</summary>
        LaunchFailed,
    }

    /// <summary>What <see cref="EnsureRunning"/> did, with a message for the status line.</summary>
    public readonly record struct Result(Outcome Outcome, string Message);

    /// <summary>The process this launcher started, if any. Never set for one that was already running.</summary>
    private Process? _launchedProcess;

    /// <summary><c>LegacyHost\Violens.LegacyExtensionHost.exe</c> beside the app's executable.</summary>
    public static string ExpectedExecutablePath =>
        Path.Combine(AppContext.BaseDirectory, LegacyHostSubdirectoryName, LegacyHostExecutableName);

    /// <summary>
    /// Starts the host unless one is already listening, so there's never a
    /// second instance competing for the pipe. Never throws.
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
            // Run the known executable directly, from its own folder.
            var startInfo = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty,
            };

            _launchedProcess = Process.Start(startInfo);
            log($"[LegacyExtensionHost] Launched '{path}' -- waiting for its session bridge to come up...");

            // Wait for its pipe, so this Start can use it.
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
    /// Closes the host only if this launcher started it: asks the window to
    /// close (so extensions' OnBeforeUnload runs), then kills it after a short
    /// wait. Never throws.
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
