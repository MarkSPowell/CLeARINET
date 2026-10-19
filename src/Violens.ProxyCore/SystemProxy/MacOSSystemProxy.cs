using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;

namespace Violens.ProxyCore.SystemProxy;

/// <summary>
/// The macOS half of "System proxy registration" in the Interception
/// Certificate Design doc's own "Platform status" section -- see that
/// section for the full reasoning behind every choice here (why per
/// network-*service* iteration rather than one global setting the way
/// <see cref="WinInetSystemProxy"/> works on Windows, why bypass-domain
/// lists are deliberately left untouched this pass, and the still-open
/// question of whether <c>networksetup</c> needs admin elevation that this
/// session had no way to confirm).
///
/// Same crash-safety shape as <see cref="WinInetSystemProxy"/>: <see cref="Enable"/>
/// backs up whatever every network service's proxy settings were before
/// touching any of them, and <see cref="Disable"/> restores from that backup
/// and deletes it. <see cref="RecoverFromCrash"/> is meant to be called once,
/// early, on every launch, on both platforms -- see
/// <see cref="SystemProxyController"/> for the facade that makes that a
/// single, platform-agnostic call site.
/// </summary>
public static class MacOSSystemProxy
{
    private static string BackupFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VioLens",
        "system-proxy-backup-macos.json");

    /// <summary>
    /// Points every enabled network service's HTTP and HTTPS proxy at
    /// <c>127.0.0.1:port</c> and turns both on. A no-op on anything but
    /// macOS. Safe to call more than once in a row without an intervening
    /// <see cref="Disable"/> -- only the first call in such a run writes a
    /// backup, matching <see cref="WinInetSystemProxy.Enable"/>'s own
    /// re-entrancy guarantee.
    /// </summary>
    [SupportedOSPlatform("macos")]
    public static void Enable(int port)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var serviceNames = ListEnabledNetworkServices();

        if (!File.Exists(BackupFilePath))
        {
            WriteBackup(new SystemProxyBackup
            {
                ManagedPort = port,
                Services = serviceNames.Select(CaptureServiceBackup).ToList(),
            });
        }

        foreach (var serviceName in serviceNames)
        {
            RunNetworkSetupCommand(BuildSetProxyArguments(serviceName, "127.0.0.1", port, secure: false));
            RunNetworkSetupCommand(BuildSetProxyStateArguments(serviceName, enabled: true, secure: false));
            RunNetworkSetupCommand(BuildSetProxyArguments(serviceName, "127.0.0.1", port, secure: true));
            RunNetworkSetupCommand(BuildSetProxyStateArguments(serviceName, enabled: true, secure: true));

            // An automatic proxy configuration script, or automatic proxy
            // discovery, would take priority over the proxy just set, so
            // turn both off for the capture; the backup puts them back.
            TryRunNetworkSetupCommand(BuildSetAutoProxyStateArguments(serviceName, enabled: false));
            TryRunNetworkSetupCommand(BuildSetProxyAutoDiscoveryArguments(serviceName, enabled: false));
        }
    }

    /// <summary>
    /// The HTTPS (or, failing that, HTTP) proxy an enabled network service
    /// is set to use right now, for VioLens to forward through (see
    /// <see cref="Proxy.UpstreamProxy"/>). Call before <see cref="Enable"/>
    /// replaces the setting with VioLens itself. Ignores a setting that
    /// points at VioLens's own <paramref name="ownPort"/>. A service using
    /// an automatic proxy configuration script or automatic proxy discovery
    /// gets a <see cref="Proxy.AutoConfig.ProxyAutoConfig"/> instead, with
    /// its manual proxy (if any) as the fallback.
    /// </summary>
    [SupportedOSPlatform("macos")]
    public static UpstreamProxyDetection DetectUpstream(int ownPort)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return UpstreamProxyDetection.None;
        }

        foreach (var serviceName in ListEnabledNetworkServices())
        {
            var settings = CaptureServiceBackup(serviceName);
            var proxy =
                (settings.SecureWebProxyEnabled && settings.SecureWebProxyServer is { Length: > 0 } secureServer && settings.SecureWebProxyPort is { } securePort
                    ? new Proxy.UpstreamProxy(secureServer, securePort)
                    : null) ??
                (settings.WebProxyEnabled && settings.WebProxyServer is { Length: > 0 } webServer && settings.WebProxyPort is { } webPort
                    ? new Proxy.UpstreamProxy(webServer, webPort)
                    : null);
            if (proxy is not null && proxy.IsSelf(ownPort))
            {
                proxy = null;
            }

            var scriptUrl = settings.AutoProxyEnabled == true ? settings.AutoProxyUrl : null;
            if (settings.AutoDiscoveryEnabled == true || !string.IsNullOrWhiteSpace(scriptUrl))
            {
                return UpstreamProxyDetection.Automatic(
                    Proxy.AutoConfig.ProxyAutoConfig.Create(settings.AutoDiscoveryEnabled == true, scriptUrl, proxy),
                    $"{serviceName} proxy settings");
            }

            if (proxy is not null)
            {
                return new UpstreamProxyDetection(proxy, $"forwarding through {proxy} ({serviceName} proxy settings)");
            }
        }

        return UpstreamProxyDetection.None;
    }

    /// <summary>
    /// Restores every network service's proxy settings to whatever
    /// <see cref="Enable"/> last backed up, and deletes the backup file. A
    /// no-op on anything but macOS. If no backup file exists -- Disable
    /// called without a matching Enable, or it was already cleaned up --
    /// the safe fallback is just turning each currently-enabled service's
    /// proxy state off, the same "don't leave it pointed at a dead port"
    /// posture <see cref="WinInetSystemProxy.Disable"/> takes.
    /// </summary>
    [SupportedOSPlatform("macos")]
    public static void Disable()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var backup = ReadBackup();

        if (backup is not null)
        {
            foreach (var service in backup.Services)
            {
                RestoreServiceBackup(service);
            }
        }
        else
        {
            foreach (var serviceName in ListEnabledNetworkServices())
            {
                RunNetworkSetupCommand(BuildSetProxyStateArguments(serviceName, enabled: false, secure: false));
                RunNetworkSetupCommand(BuildSetProxyStateArguments(serviceName, enabled: false, secure: true));
            }
        }

        DeleteBackupFile();
    }

    /// <summary>
    /// Call once, early, on every launch, before anything else in this
    /// class runs -- see <see cref="WinInetSystemProxy.RecoverFromCrash"/>'s
    /// own remarks, which apply here unchanged, just against
    /// <c>networksetup</c> instead of the registry.
    /// </summary>
    [SupportedOSPlatform("macos")]
    public static void RecoverFromCrash()
    {
        if (!OperatingSystem.IsMacOS() || !File.Exists(BackupFilePath))
        {
            return;
        }

        Disable();
    }

    [SupportedOSPlatform("macos")]
    private static List<string> ListEnabledNetworkServices()
    {
        var output = RunNetworkSetupCommand(BuildListAllNetworkServicesArguments());
        return ParseNetworkServiceNames(output);
    }

    [SupportedOSPlatform("macos")]
    private static NetworkServiceProxyBackup CaptureServiceBackup(string serviceName)
    {
        var web = ParseProxyState(RunNetworkSetupCommand(BuildGetProxyArguments(serviceName, secure: false)));
        var secureWeb = ParseProxyState(RunNetworkSetupCommand(BuildGetProxyArguments(serviceName, secure: true)));
        var autoProxy = TryRunNetworkSetupCommand(BuildGetAutoProxyUrlArguments(serviceName)) is { } autoProxyOutput
            ? ParseAutoProxyUrl(autoProxyOutput)
            : ((string? Url, bool Enabled)?)null;
        var autoDiscovery = TryRunNetworkSetupCommand(BuildGetProxyAutoDiscoveryArguments(serviceName)) is { } discoveryOutput
            ? ParseProxyAutoDiscovery(discoveryOutput)
            : null;

        return new NetworkServiceProxyBackup
        {
            AutoProxyUrl = autoProxy?.Url,
            AutoProxyEnabled = autoProxy?.Enabled,
            AutoDiscoveryEnabled = autoDiscovery,
            ServiceName = serviceName,
            WebProxyEnabled = web.Enabled,
            WebProxyServer = web.Server,
            WebProxyPort = web.Port,
            SecureWebProxyEnabled = secureWeb.Enabled,
            SecureWebProxyServer = secureWeb.Server,
            SecureWebProxyPort = secureWeb.Port,
        };
    }

    [SupportedOSPlatform("macos")]
    private static void RestoreServiceBackup(NetworkServiceProxyBackup service)
    {
        // Only re-point the server/port if one was actually configured
        // before -- a service that had no proxy server set at all
        // shouldn't gain one just because VioLens is cleaning up after
        // itself. Either way the enabled/disabled state below is restored
        // unconditionally, which is what actually matters for not leaving
        // traffic pointed at a dead port.
        if (service.WebProxyServer is not null && service.WebProxyPort is not null)
        {
            RunNetworkSetupCommand(BuildSetProxyArguments(service.ServiceName, service.WebProxyServer, service.WebProxyPort.Value, secure: false));
        }

        RunNetworkSetupCommand(BuildSetProxyStateArguments(service.ServiceName, service.WebProxyEnabled, secure: false));

        if (service.SecureWebProxyServer is not null && service.SecureWebProxyPort is not null)
        {
            RunNetworkSetupCommand(BuildSetProxyArguments(service.ServiceName, service.SecureWebProxyServer, service.SecureWebProxyPort.Value, secure: true));
        }

        RunNetworkSetupCommand(BuildSetProxyStateArguments(service.ServiceName, service.SecureWebProxyEnabled, secure: true));

        // Null in a backup from an older version, which didn't change these.
        if (service.AutoProxyEnabled is { } autoProxyEnabled)
        {
            if (service.AutoProxyUrl is not null)
            {
                TryRunNetworkSetupCommand(BuildSetAutoProxyUrlArguments(service.ServiceName, service.AutoProxyUrl));
            }

            TryRunNetworkSetupCommand(BuildSetAutoProxyStateArguments(service.ServiceName, autoProxyEnabled));
        }

        if (service.AutoDiscoveryEnabled is { } autoDiscoveryEnabled)
        {
            TryRunNetworkSetupCommand(BuildSetProxyAutoDiscoveryArguments(service.ServiceName, autoDiscoveryEnabled));
        }
    }

    /// <summary>
    /// Internal rather than private purely so
    /// <c>MacOSSystemProxyTests</c> can assert on the exact argument list
    /// without actually invoking <c>networksetup</c> -- see
    /// <c>Certificates.MacOSCertificateTrust.BuildAddTrustedCertArguments</c>'s
    /// own remarks on why this is <c>internal</c> and the design doc's
    /// "Testing scope, deliberately narrow" note.
    /// </summary>
    internal static string[] BuildListAllNetworkServicesArguments() => ["-listallnetworkservices"];

    /// <summary>
    /// <c>secure</c> selects <c>-getsecurewebproxy</c> (HTTPS) over
    /// <c>-getwebproxy</c> (HTTP) -- macOS tracks these as two entirely
    /// separate settings per service, unlike WinINET's single
    /// <c>ProxyServer</c> value covering both.
    /// </summary>
    internal static string[] BuildGetProxyArguments(string serviceName, bool secure) =>
        [secure ? "-getsecurewebproxy" : "-getwebproxy", serviceName];

    internal static string[] BuildSetProxyArguments(string serviceName, string host, int port, bool secure) =>
        [secure ? "-setsecurewebproxy" : "-setwebproxy", serviceName, host, port.ToString(CultureInfo.InvariantCulture)];

    internal static string[] BuildSetProxyStateArguments(string serviceName, bool enabled, bool secure) =>
        [secure ? "-setsecurewebproxystate" : "-setwebproxystate", serviceName, enabled ? "on" : "off"];

    internal static string[] BuildGetAutoProxyUrlArguments(string serviceName) => ["-getautoproxyurl", serviceName];

    internal static string[] BuildSetAutoProxyUrlArguments(string serviceName, string url) => ["-setautoproxyurl", serviceName, url];

    internal static string[] BuildSetAutoProxyStateArguments(string serviceName, bool enabled) =>
        ["-setautoproxystate", serviceName, enabled ? "on" : "off"];

    internal static string[] BuildGetProxyAutoDiscoveryArguments(string serviceName) => ["-getproxyautodiscovery", serviceName];

    internal static string[] BuildSetProxyAutoDiscoveryArguments(string serviceName, bool enabled) =>
        ["-setproxyautodiscovery", serviceName, enabled ? "on" : "off"];

    /// <summary>
    /// Parses <c>networksetup -getautoproxyurl</c>'s output:
    /// <code>
    /// URL: http://wpad.example.com/proxy.pac
    /// Enabled: Yes
    /// </code>
    /// with "URL: (null)" when none is set.
    /// </summary>
    internal static (string? Url, bool Enabled) ParseAutoProxyUrl(string output)
    {
        string? url = null;
        var enabled = false;
        foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("URL:", StringComparison.Ordinal))
            {
                var value = line["URL:".Length..].Trim();
                url = value.Length == 0 || value == "(null)" ? null : value;
            }
            else if (line.StartsWith("Enabled:", StringComparison.Ordinal))
            {
                enabled = line["Enabled:".Length..].Trim().Equals("Yes", StringComparison.OrdinalIgnoreCase);
            }
        }

        return (url, enabled);
    }

    /// <summary>Parses <c>networksetup -getproxyautodiscovery</c>'s "Auto Proxy Discovery: On" (or Off).</summary>
    internal static bool? ParseProxyAutoDiscovery(string output)
    {
        var colon = output.IndexOf(':');
        if (colon < 0)
        {
            return null;
        }

        var value = output[(colon + 1)..].Trim();
        if (value.Equals("On", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return value.Equals("Off", StringComparison.OrdinalIgnoreCase) ? false : null;
    }

    /// <summary>
    /// Parses <c>networksetup -listallnetworkservices</c>' output: a
    /// human-readable header line ("An asterisk (*) denotes that a network
    /// service is disabled.") followed by one service name per line, with
    /// disabled services prefixed <c>*</c>. Only enabled services are
    /// returned -- VioLens has nothing useful to configure on a service
    /// the user isn't actually using. Internal for the same testability
    /// reason as the argument-builders above.
    /// </summary>
    internal static List<string> ParseNetworkServiceNames(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return lines
            .Skip(1)
            .Where(line => !line.StartsWith('*'))
            .Where(line => line.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Parses <c>networksetup -getwebproxy</c>/<c>-getsecurewebproxy</c>'s
    /// fixed-format output:
    /// <code>
    /// Enabled: Yes
    /// Server: 127.0.0.1
    /// Port: 8888
    /// Authenticated Proxy Enabled: 0
    /// </code>
    /// An empty <c>Server:</c> line (nothing configured) parses as a null
    /// server and null port, not empty-string/zero, so
    /// <see cref="RestoreServiceBackup"/> can tell "nothing was configured
    /// before" apart from "port 0 was configured before". Internal for the
    /// same testability reason as the argument-builders above.
    /// </summary>
    internal static ProxyState ParseProxyState(string output)
    {
        var enabled = false;
        string? server = null;
        int? port = null;

        foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();

            if (line.StartsWith("Enabled:", StringComparison.Ordinal))
            {
                enabled = line["Enabled:".Length..].Trim().Equals("Yes", StringComparison.OrdinalIgnoreCase);
            }
            else if (line.StartsWith("Server:", StringComparison.Ordinal))
            {
                var value = line["Server:".Length..].Trim();
                server = value.Length > 0 ? value : null;
            }
            else if (line.StartsWith("Port:", StringComparison.Ordinal))
            {
                var value = line["Port:".Length..].Trim();
                port = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed != 0
                    ? parsed
                    : null;
            }
        }

        // A server with no port (shouldn't happen in practice, but the
        // parse above is line-by-line and defensive) isn't something
        // RestoreServiceBackup can act on -- treat it the same as nothing
        // configured rather than calling -setwebproxy with a garbage port.
        if (server is not null && port is null)
        {
            server = null;
        }

        return new ProxyState(enabled, server, port);
    }

    /// <summary>
    /// For the automatic-proxy settings, which a service may not support:
    /// returns null instead of throwing when <c>networksetup</c> fails.
    /// </summary>
    [SupportedOSPlatform("macos")]
    private static string? TryRunNetworkSetupCommand(string[] arguments)
    {
        try
        {
            return RunNetworkSetupCommand(arguments);
        }
        catch (SystemProxyCommandException ex)
        {
            Console.WriteLine($"[proxy] {ex.Message}");
            return null;
        }
    }

    [SupportedOSPlatform("macos")]
    private static string RunNetworkSetupCommand(string[] arguments)
    {
        var startInfo = new ProcessStartInfo("networksetup")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new SystemProxyCommandException("Failed to start the 'networksetup' process.", exitCode: -1, standardError: string.Empty);

        // Same deadlock-avoidance shape as
        // Certificates.MacOSCertificateTrust.RunSecurityCommand: both
        // streams read to completion before WaitForExit, not after.
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new SystemProxyCommandException(
                $"'networksetup {string.Join(' ', arguments)}' exited with code {process.ExitCode}.",
                process.ExitCode,
                standardError);
        }

        return standardOutput;
    }

    private static SystemProxyBackup? ReadBackup()
    {
        try
        {
            if (!File.Exists(BackupFilePath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<SystemProxyBackup>(File.ReadAllText(BackupFilePath));
        }
        catch
        {
            // A corrupt or partially-written backup file is exactly the
            // kind of thing a crash could leave behind. Treating it as "no
            // backup" falls through to Disable's safe fallback (turn every
            // service's proxy state off) instead of throwing during what's
            // meant to be cleanup -- matches
            // WinInetSystemProxy.ReadBackup's own posture.
            return null;
        }
    }

    private static void WriteBackup(SystemProxyBackup backup)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(BackupFilePath)!);
        File.WriteAllText(BackupFilePath, JsonSerializer.Serialize(backup));
    }

    private static void DeleteBackupFile()
    {
        try
        {
            File.Delete(BackupFilePath);
        }
        catch
        {
            // Best-effort -- matches WinInetSystemProxy.DeleteBackupFile's
            // own posture on this exact kind of non-load-bearing cleanup.
        }
    }

    /// <summary>
    /// Plain-property shapes on purpose -- the safest one for
    /// <see cref="JsonSerializer"/> to round-trip without depending on
    /// constructor-parameter matching, matching
    /// <see cref="WinInetSystemProxy.ProxyBackup"/>'s own shape. Internal
    /// rather than private so there's no question of System.Text.Json's
    /// default reflection-based (de)serializer being able to see it.
    /// </summary>
    internal sealed class SystemProxyBackup
    {
        public int ManagedPort { get; set; }

        public List<NetworkServiceProxyBackup> Services { get; set; } = [];
    }

    internal sealed class NetworkServiceProxyBackup
    {
        public string ServiceName { get; set; } = string.Empty;

        public bool WebProxyEnabled { get; set; }

        public string? WebProxyServer { get; set; }

        public int? WebProxyPort { get; set; }

        public bool SecureWebProxyEnabled { get; set; }

        public string? SecureWebProxyServer { get; set; }

        public int? SecureWebProxyPort { get; set; }

        /// <summary>The automatic proxy configuration script's URL ("Automatic proxy configuration").</summary>
        public string? AutoProxyUrl { get; set; }

        /// <summary>Whether that script was on; null in a backup from an older version.</summary>
        public bool? AutoProxyEnabled { get; set; }

        /// <summary>"Auto proxy discovery"; null in a backup from an older version.</summary>
        public bool? AutoDiscoveryEnabled { get; set; }
    }

    /// <summary>
    /// The parsed result of a single <c>-getwebproxy</c>/
    /// <c>-getsecurewebproxy</c> call. A record struct rather than a tuple
    /// purely for readability at <see cref="ParseProxyState"/>'s call
    /// sites and in <c>MacOSSystemProxyTests</c>' own assertions.
    /// </summary>
    internal readonly record struct ProxyState(bool Enabled, string? Server, int? Port);
}
