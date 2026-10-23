using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Win32;

namespace Violens.ProxyCore.SystemProxy;

/// <summary>
/// Sets the current user's Windows proxy the way Fiddler Classic does:
/// the WinINET registry values, then a settings-changed broadcast so running
/// apps pick it up. The machine-wide WinHTTP proxy is left alone, which
/// keeps services out of the capture.
///
/// <see cref="Enable"/> backs up the previous settings to a file first and
/// <see cref="Disable"/> restores and deletes it. A backup still there at
/// launch means the last run crashed; <see cref="RecoverFromCrash"/>
/// restores it.
/// </summary>
internal static class WinInetSystemProxy
{
    private const string InternetSettingsKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const int InternetOptionSettingsChanged = 39;
    private const int InternetOptionRefresh = 37;

    // In the host app's data folder (see ProxyHostIdentity), read each time
    // so ProxyEngine.Configure can come before or after this class is first used.
    private static string BackupFilePath =>
        Path.Combine(ProxyEngine.Identity.ResolveDataFolder(), "system-proxy-backup.json");

    /// <summary>
    /// Points the system proxy at <c>127.0.0.1:port</c>. Calling it twice
    /// doesn't overwrite the first backup with VioLens's own settings.
    /// </summary>
    public static void Enable(int port)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var key = OpenInternetSettingsKey();
        var connection = TryReadConnectionOptions();

        if (!File.Exists(BackupFilePath))
        {
            WriteBackup(new ProxyBackup
            {
                ProxyEnable = Convert.ToInt32(key.GetValue("ProxyEnable", 0)) != 0,
                ProxyServer = key.GetValue("ProxyServer") as string,
                ProxyOverride = key.GetValue("ProxyOverride") as string,
                ManagedPort = port,
                ConnectionFlags = connection?.Flags,
                AutoConfigUrl = connection?.AutoConfigUrl,
            });
        }

        var server = $"127.0.0.1:{port}";
        var bypass = MergeBypassList(key.GetValue("ProxyOverride") as string);
        key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
        key.SetValue("ProxyServer", server, RegistryValueKind.String);
        key.SetValue("ProxyOverride", bypass, RegistryValueKind.String);

        // "Automatically detect settings" and "Use setup script" take
        // priority over the manual proxy, so while either is on, browsers
        // would never reach VioLens. Turn them off for the capture (the
        // backup above puts them back). The setup script's address is kept.
        if (connection is not null &&
            (connection.Flags & (WinInetConnectionOptions.AutoDetect | WinInetConnectionOptions.AutoProxyUrl)) != 0)
        {
            WinInetConnectionOptions.Write(new WinInetConnectionOptions.Settings(
                WinInetConnectionOptions.Direct | WinInetConnectionOptions.ManualProxy, server, bypass, AutoConfigUrl: null));
        }

        BroadcastSettingsChanged();
    }

    /// <summary>
    /// The proxy Windows uses now, to forward through. Call before
    /// <see cref="Enable"/>. A setting pointing at VioLens itself is ignored.
    /// With automatic detection or a setup script on, returns a
    /// <see cref="Proxy.AutoConfig.ProxyAutoConfig"/> that starts loading the
    /// script, with any manual proxy as the fallback.
    /// </summary>
    public static UpstreamProxyDetection DetectUpstream(int ownPort)
    {
        if (!OperatingSystem.IsWindows())
        {
            return UpstreamProxyDetection.None;
        }

        using var key = OpenInternetSettingsKey();
        var connection = TryReadConnectionOptions();

        Proxy.UpstreamProxy? manual;
        bool autoDetect;
        string? scriptUrl;
        if (connection is not null)
        {
            manual = (connection.Flags & WinInetConnectionOptions.ManualProxy) != 0
                ? Proxy.UpstreamProxy.FromWindowsSettings(connection.ProxyServer, connection.ProxyBypass)
                : null;
            autoDetect = (connection.Flags & WinInetConnectionOptions.AutoDetect) != 0;
            scriptUrl = (connection.Flags & WinInetConnectionOptions.AutoProxyUrl) != 0 ? connection.AutoConfigUrl : null;
        }
        else
        {
            // Couldn't use the API: the registry values have the manual
            // proxy and the script address, but not "Automatically detect".
            manual = Convert.ToInt32(key.GetValue("ProxyEnable", 0)) != 0
                ? Proxy.UpstreamProxy.FromWindowsSettings(key.GetValue("ProxyServer") as string, key.GetValue("ProxyOverride") as string)
                : null;
            autoDetect = false;
            scriptUrl = key.GetValue("AutoConfigURL") as string;
        }

        if (manual is not null && manual.IsSelf(ownPort))
        {
            manual = null;
        }

        if (autoDetect || !string.IsNullOrWhiteSpace(scriptUrl))
        {
            return UpstreamProxyDetection.Automatic(Proxy.AutoConfig.ProxyAutoConfig.Create(autoDetect, scriptUrl, manual), "Windows proxy settings");
        }

        return manual is not null
            ? new UpstreamProxyDetection(manual, $"forwarding through {manual} (Windows proxy settings)")
            : UpstreamProxyDetection.None;
    }

    /// <summary>
    /// Restores the backed-up settings and deletes the backup. Without a
    /// backup, turns the proxy off rather than leave it pointing at a dead
    /// port.
    /// </summary>
    public static void Disable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var backup = ReadBackup();
        using (var key = OpenInternetSettingsKey())
        {
            if (backup is not null)
            {
                key.SetValue("ProxyEnable", backup.ProxyEnable ? 1 : 0, RegistryValueKind.DWord);
                SetOrRemove(key, "ProxyServer", backup.ProxyServer);
                SetOrRemove(key, "ProxyOverride", backup.ProxyOverride);

                // Enable only touched these when automatic detection or a
                // setup script was on; put them back exactly.
                if (backup.ConnectionFlags is { } flags &&
                    (flags & (WinInetConnectionOptions.AutoDetect | WinInetConnectionOptions.AutoProxyUrl)) != 0)
                {
                    try
                    {
                        WinInetConnectionOptions.Write(new WinInetConnectionOptions.Settings(
                            flags, backup.ProxyServer ?? string.Empty, backup.ProxyOverride ?? string.Empty, backup.AutoConfigUrl ?? string.Empty));
                    }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException)
                    {
                        // The manual settings above are restored either way;
                        // say what couldn't be, rather than fail Stop.
                        ProxyEngine.Log("system-proxy",
                            $"Couldn't turn automatic proxy settings back on ({ex.Message}); " +
                            "check Settings > Network & internet > Proxy.");
                    }
                }
            }
            else
            {
                key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
            }
        }

        BroadcastSettingsChanged();
        DeleteBackupFile();
    }

    /// <summary>Call once at launch: restores a backup left by a run that didn't stop cleanly.</summary>
    public static void RecoverFromCrash()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(BackupFilePath))
        {
            return;
        }

        Disable();
    }

    // Only called from guarded methods; the attribute tells the analyzer so.
    [SupportedOSPlatform("windows")]
    private static WinInetConnectionOptions.Settings? TryReadConnectionOptions()
    {
        try
        {
            return WinInetConnectionOptions.Read();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static RegistryKey OpenInternetSettingsKey() =>
        Registry.CurrentUser.OpenSubKey(InternetSettingsKeyPath, writable: true)
            ?? throw new InvalidOperationException($"Could not open registry key '{InternetSettingsKeyPath}'.");

    /// <summary>Internal for tests.</summary>
    internal static string MergeBypassList(string? existing)
    {
        if (string.IsNullOrEmpty(existing))
        {
            return "<local>";
        }

        var entries = existing.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return entries.Any(entry => string.Equals(entry, "<local>", StringComparison.OrdinalIgnoreCase))
            ? existing
            : existing + ";<local>";
    }

    [SupportedOSPlatform("windows")]
    private static void SetOrRemove(RegistryKey key, string name, string? value)
    {
        if (value is null)
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }
        else
        {
            key.SetValue(name, value, RegistryValueKind.String);
        }
    }

    private static ProxyBackup? ReadBackup()
    {
        try
        {
            if (!File.Exists(BackupFilePath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<ProxyBackup>(File.ReadAllText(BackupFilePath));
        }
        catch
        {
            // A crash can leave a corrupt backup; treat it as none, so Disable
            // turns the proxy off instead of throwing.
            return null;
        }
    }

    private static void WriteBackup(ProxyBackup backup)
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
            // Best effort: a leftover file just means the same restore runs again
            // next launch.
        }
    }

    private static void BroadcastSettingsChanged()
    {
        InternetSetOption(0, InternetOptionSettingsChanged, 0, 0);
        InternetSetOption(0, InternetOptionRefresh, 0, 0);
    }

    // The W export named explicitly. DllImport rather than LibraryImport,
    // which would need AllowUnsafeBlocks for the whole project.
    [DllImport("wininet.dll", EntryPoint = "InternetSetOptionW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InternetSetOption(nint hInternet, int dwOption, nint lpBuffer, int dwBufferLength);

    /// <summary>Settable properties, so System.Text.Json round-trips it simply.</summary>
    internal sealed class ProxyBackup
    {
        public bool ProxyEnable { get; set; }

        public string? ProxyServer { get; set; }

        public string? ProxyOverride { get; set; }

        public int ManagedPort { get; set; }

        /// <summary>The LAN connection's proxy flags before capture (null in a backup from an older version).</summary>
        public int? ConnectionFlags { get; set; }

        /// <summary>The "Use setup script" address before capture.</summary>
        public string? AutoConfigUrl { get; set; }
    }
}
