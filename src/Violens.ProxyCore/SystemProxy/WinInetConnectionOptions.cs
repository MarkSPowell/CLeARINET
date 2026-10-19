using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Violens.ProxyCore.SystemProxy;

/// <summary>
/// Windows' per-connection proxy settings for the LAN connection, read and
/// written through WinINET's <c>INTERNET_OPTION_PER_CONNECTION_OPTION</c>,
/// the documented API behind the Proxy page in Settings. Unlike the
/// <c>ProxyEnable</c>/<c>ProxyServer</c> registry values, this covers
/// "Automatically detect settings" and "Use setup script", which take
/// priority over a manual proxy: while either is on, browsers ignore
/// VioLens as the manual proxy, so capture turns them off and puts them
/// back afterwards.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WinInetConnectionOptions
{
    /// <summary>PROXY_TYPE_DIRECT.</summary>
    public const int Direct = 0x1;

    /// <summary>PROXY_TYPE_PROXY: use the manual proxy.</summary>
    public const int ManualProxy = 0x2;

    /// <summary>PROXY_TYPE_AUTO_PROXY_URL: "Use setup script".</summary>
    public const int AutoProxyUrl = 0x4;

    /// <summary>PROXY_TYPE_AUTO_DETECT: "Automatically detect settings".</summary>
    public const int AutoDetect = 0x8;

    private const int InternetOptionPerConnectionOption = 75;
    private const int OptionFlags = 1;
    private const int OptionProxyServer = 2;
    private const int OptionProxyBypass = 3;
    private const int OptionAutoConfigUrl = 4;

    // INTERNET_PER_CONN_FLAGS_UI: on current Windows, the flags as the
    // Settings page shows them. INTERNET_PER_CONN_FLAGS can leave out
    // "Automatically detect settings", so this is read first.
    private const int OptionFlagsUi = 10;

    /// <summary>The settings; a null string is left unchanged by <see cref="Write"/>.</summary>
    public sealed record Settings(int Flags, string? ProxyServer, string? ProxyBypass, string? AutoConfigUrl);

    /// <summary>The current LAN settings. Throws <see cref="Win32Exception"/> if WinINET refuses.</summary>
    public static Settings Read() =>
        TryRead(OptionFlagsUi, out var settings) || TryRead(OptionFlags, out settings)
            ? settings!
            : throw new Win32Exception(Marshal.GetLastWin32Error(), "Couldn't read the Windows proxy settings.");

    /// <summary>Applies <paramref name="settings"/>. Throws <see cref="Win32Exception"/> if WinINET refuses.</summary>
    public static void Write(Settings settings)
    {
        if (!TryWrite(settings, includeUiFlags: true) && !TryWrite(settings, includeUiFlags: false))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Couldn't change the Windows proxy settings.");
        }
    }

    private static bool TryRead(int flagsOption, out Settings? settings)
    {
        settings = null;
        int[] ids = [flagsOption, OptionProxyServer, OptionProxyBypass, OptionAutoConfigUrl];
        var options = AllocateOptions(ids);
        try
        {
            var list = NewList(options, ids.Length);
            var size = list.Size;
            if (!InternetQueryOption(IntPtr.Zero, InternetOptionPerConnectionOption, ref list, ref size))
            {
                return false;
            }

            settings = new Settings(
                Marshal.ReadInt32(options, ValueOffset),
                TakeString(options, 1),
                TakeString(options, 2),
                TakeString(options, 3));
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(options);
        }
    }

    private static bool TryWrite(Settings settings, bool includeUiFlags)
    {
        var values = new List<(int Id, string? Text)> { (OptionFlags, null) };
        if (settings.ProxyServer is not null)
        {
            values.Add((OptionProxyServer, settings.ProxyServer));
        }

        if (settings.ProxyBypass is not null)
        {
            values.Add((OptionProxyBypass, settings.ProxyBypass));
        }

        if (settings.AutoConfigUrl is not null)
        {
            values.Add((OptionAutoConfigUrl, settings.AutoConfigUrl));
        }

        var ids = values.Select(v => v.Id).ToList();
        if (includeUiFlags)
        {
            ids.Add(OptionFlagsUi);
        }

        var options = AllocateOptions(ids.ToArray());
        var strings = new List<IntPtr>();
        try
        {
            Marshal.WriteInt32(options, ValueOffset, settings.Flags);
            if (includeUiFlags)
            {
                Marshal.WriteInt32(options, (values.Count * OptionSize) + ValueOffset, settings.Flags);
            }

            for (var i = 1; i < values.Count; i++)
            {
                var text = Marshal.StringToHGlobalUni(values[i].Text);
                strings.Add(text);
                Marshal.WriteIntPtr(options, (i * OptionSize) + ValueOffset, text);
            }

            var list = NewList(options, ids.Count);
            return InternetSetOption(IntPtr.Zero, InternetOptionPerConnectionOption, ref list, list.Size);
        }
        finally
        {
            foreach (var text in strings)
            {
                Marshal.FreeHGlobal(text);
            }

            Marshal.FreeHGlobal(options);
        }
    }

    // INTERNET_PER_CONN_OPTIONW is { DWORD dwOption; union { DWORD; LPWSTR; FILETIME } Value; }:
    // the union is pointer-aligned, so it starts at 8 on 64-bit (16 bytes in
    // all) and at 4 on 32-bit (12 bytes, the FILETIME being 8).
    private static int ValueOffset => IntPtr.Size == 8 ? 8 : 4;

    private static int OptionSize => IntPtr.Size == 8 ? 16 : 12;

    private static IntPtr AllocateOptions(int[] ids)
    {
        var bytes = OptionSize * ids.Length;
        var options = Marshal.AllocHGlobal(bytes);
        Marshal.Copy(new byte[bytes], 0, options, bytes);
        for (var i = 0; i < ids.Length; i++)
        {
            Marshal.WriteInt32(options, i * OptionSize, ids[i]);
        }

        return options;
    }

    private static OptionList NewList(IntPtr options, int count) => new()
    {
        Size = Marshal.SizeOf<OptionList>(),
        Connection = IntPtr.Zero, // the LAN connection
        OptionCount = count,
        OptionError = 0,
        Options = options,
    };

    /// <summary>Reads a string WinINET returned, and frees it (WinINET allocates these with GlobalAlloc).</summary>
    private static string? TakeString(IntPtr options, int index)
    {
        var pointer = Marshal.ReadIntPtr(options, (index * OptionSize) + ValueOffset);
        if (pointer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            GlobalFree(pointer);
        }
    }

    /// <summary>INTERNET_PER_CONN_OPTION_LISTW.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct OptionList
    {
        public int Size;
        public IntPtr Connection;
        public int OptionCount;
        public int OptionError;
        public IntPtr Options;
    }

    [DllImport("wininet.dll", EntryPoint = "InternetQueryOptionW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InternetQueryOption(IntPtr internet, int option, ref OptionList buffer, ref int bufferLength);

    [DllImport("wininet.dll", EntryPoint = "InternetSetOptionW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InternetSetOption(IntPtr internet, int option, ref OptionList buffer, int bufferLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr memory);
}
