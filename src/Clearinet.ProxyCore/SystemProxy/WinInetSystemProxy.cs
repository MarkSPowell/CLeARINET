using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Win32;

namespace Clearinet.ProxyCore.SystemProxy;

/// <summary>
/// Registers CLeARINET as the current user's Windows system proxy, the same
/// way Fiddler Classic's Start/Stop capture toggle does: writing the
/// WinINET registry values under <c>...\Internet Settings</c> and
/// broadcasting <c>INTERNET_OPTION_SETTINGS_CHANGED</c>/<c>_REFRESH</c> so
/// already-running WinINET-aware processes (Internet Explorer, Chromium
/// browsers via their own IE-proxy-config reader, .NET's default proxy
/// resolution, and so on) pick it up immediately, no logoff required.
///
/// This only affects applications that consult that particular registry
/// store in the first place -- a separate, machine-wide WinHTTP proxy store
/// (configured via <c>netsh winhttp</c>, used by services and some system
/// components) is deliberately left untouched, which is a large part of why
/// this doesn't drag unrelated background processes through the proxy too.
///
/// The other half of the design is crash safety: <see cref="Enable"/>
/// backs up whatever was there before to a small JSON file under the local
/// app-data folder *before* overwriting anything, and <see cref="Disable"/>
/// restores from it and deletes it. If CLeARINET's process dies before
/// Disable ever runs -- a crash, a kill, Windows tearing it down without
/// warning -- that backup file survives on disk. <see cref="RecoverFromCrash"/>
/// is meant to be called once, early, on the next launch: finding the file
/// still there is exactly the signal that the previous run never cleaned up
/// after itself, so it restores from it before this run touches the
/// registry at all.
/// </summary>
public static class WinInetSystemProxy
{
    private const string InternetSettingsKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const int InternetOptionSettingsChanged = 39;
    private const int InternetOptionRefresh = 37;

    private static string BackupFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CLeARINET",
        "system-proxy-backup.json");

    /// <summary>
    /// Points the system proxy at <c>127.0.0.1:port</c>. A no-op on
    /// anything but Windows. Safe to call more than once in a row without
    /// an intervening <see cref="Disable"/> -- only the first call in such
    /// a run writes a backup, so a second Enable (e.g. Start clicked twice
    /// through some UI race) can never overwrite the real pre-CLeARINET
    /// settings with CLeARINET's own.
    /// </summary>
    public static void Enable(int port)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var key = OpenInternetSettingsKey();

        if (!File.Exists(BackupFilePath))
        {
            WriteBackup(new ProxyBackup
            {
                ProxyEnable = Convert.ToInt32(key.GetValue("ProxyEnable", 0)) != 0,
                ProxyServer = key.GetValue("ProxyServer") as string,
                ProxyOverride = key.GetValue("ProxyOverride") as string,
                ManagedPort = port,
            });
        }

        key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
        key.SetValue("ProxyServer", $"127.0.0.1:{port}", RegistryValueKind.String);
        key.SetValue("ProxyOverride", MergeBypassList(key.GetValue("ProxyOverride") as string), RegistryValueKind.String);

        BroadcastSettingsChanged();
    }

    /// <summary>
    /// Restores whatever the system proxy was set to before <see cref="Enable"/>
    /// last ran, and deletes the backup file. A no-op on anything but
    /// Windows. If no backup file exists -- Disable called without a
    /// matching Enable, or the file was already cleaned up -- the safe
    /// fallback is just turning the proxy off, rather than risking it stay
    /// pointed at a port nothing is listening on any more.
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
            }
            else
            {
                key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
            }
        }

        BroadcastSettingsChanged();
        DeleteBackupFile();
    }

    /// <summary>
    /// Call once, early, on every launch, before anything else in this
    /// class runs. A leftover backup file is exactly the signature of a
    /// previous run that never reached its own <see cref="Disable"/> call
    /// -- this restores from it immediately. A clean previous shutdown
    /// leaves no file behind, so an ordinary cold start is a cheap
    /// <see cref="File.Exists(string)"/> check and nothing more.
    /// </summary>
    public static void RecoverFromCrash()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(BackupFilePath))
        {
            return;
        }

        Disable();
    }

    // Neither of these has its own OperatingSystem.IsWindows() guard --
    // the platform-compatibility analyzer only recognizes that pattern
    // within the guarded method itself, and both of these are private
    // helpers only ever reached from Enable/Disable, which already guard
    // before calling them. [SupportedOSPlatform] here is what tells the
    // analyzer these two are Windows-only by contract instead, which is
    // enough for it to stop warning at those (already-guarded) call sites.
    [SupportedOSPlatform("windows")]
    private static RegistryKey OpenInternetSettingsKey() =>
        Registry.CurrentUser.OpenSubKey(InternetSettingsKeyPath, writable: true)
            ?? throw new InvalidOperationException($"Could not open registry key '{InternetSettingsKeyPath}'.");

    /// <summary>
    /// Internal rather than private purely so
    /// <c>Clearinet.ProxyCore.Tests.WinInetSystemProxyTests</c> can exercise
    /// this one pure, no-registry-touching piece of the class directly --
    /// see that test file's remarks on why the rest of this class isn't
    /// unit-tested the same way.
    /// </summary>
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
            // A corrupt or partially-written backup file is exactly the
            // kind of thing a crash could leave behind. Treating it as "no
            // backup" falls through to Disable's safe fallback (turn the
            // proxy off) instead of throwing during what's meant to be
            // cleanup.
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
            // Best-effort -- a leftover (harmless, still-deletable) file
            // here just means the next RecoverFromCrash re-applies the
            // same restore it already just performed. Not worth failing
            // Disable over.
        }
    }

    private static void BroadcastSettingsChanged()
    {
        InternetSetOption(0, InternetOptionSettingsChanged, 0, 0);
        InternetSetOption(0, InternetOptionRefresh, 0, 0);
    }

    // EntryPoint pinned explicitly: wininet.dll exports InternetSetOptionA
    // and InternetSetOptionW, not a plain "InternetSetOption" symbol --
    // classic DllImport marshaling resolves that automatically when
    // ExactSpelling is left false, but naming the real W export directly
    // removes any doubt. No strings actually cross this boundary (every
    // argument here is 0/null), so A-vs-W has no behavioral effect either
    // way; W is just the unambiguous choice. DllImport rather than the
    // newer LibraryImport source generator specifically because
    // LibraryImport's generated marshaling stub needs unsafe code, which
    // would mean turning on AllowUnsafeBlocks for this whole project just
    // for one P/Invoke that doesn't marshal anything unsafe in the first
    // place.
    [DllImport("wininet.dll", EntryPoint = "InternetSetOptionW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InternetSetOption(nint hInternet, int dwOption, nint lpBuffer, int dwBufferLength);

    /// <summary>
    /// Plain-property shape on purpose -- the safest one for
    /// <see cref="JsonSerializer"/> to round-trip without depending on
    /// constructor-parameter matching. Internal rather than private so
    /// there's no question of System.Text.Json's default reflection-based
    /// (de)serializer being able to see it.
    /// </summary>
    internal sealed class ProxyBackup
    {
        public bool ProxyEnable { get; set; }

        public string? ProxyServer { get; set; }

        public string? ProxyOverride { get; set; }

        public int ManagedPort { get; set; }
    }
}
