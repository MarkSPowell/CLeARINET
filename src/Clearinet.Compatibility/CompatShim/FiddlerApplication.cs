using System.Globalization;
using Clearinet.ProxyCore.Preferences;

namespace Clearinet.CompatShim;

/// <summary>
/// The parts of Fiddler's static <c>FiddlerApplication</c> that a ported
/// extension reaches for most: its log, its preferences, and telling the
/// user something. Each routes through <see cref="CompatShimHost"/>, so the
/// desktop app decides what "show the user" means on each platform.
///
/// <see cref="UI"/> isn't Fiddler's WinForms main window, which can't exist
/// on macOS or inside CLeARINET's Avalonia window. It's a small stand-in
/// with the members extensions use (menus, session-list columns), which the
/// app draws with Avalonia. Row colours need nothing from it: extensions set
/// <c>ui-backcolor</c> and friends as session flags.
/// </summary>
public static class FiddlerApplication
{
    public static Logger Log { get; } = new();

    public static IFiddlerPreferences Prefs { get; } = new HostPreferences();

    /// <summary>
    /// The host window's extensible parts: menus and session-list columns
    /// (see <see cref="frmViewer"/>). The desktop app draws whatever
    /// extensions add here; with no app (tests, headless hosts) the items
    /// are simply kept.
    /// </summary>
    public static frmViewer UI { get; } = new();

    /// <summary>Tells the user something. The desktop app shows a message; a host with no UI logs it.</summary>
    public static void DoNotifyUser(string sMessage, string sTitle)
    {
        var notify = CompatShimHost.NotifyUser;
        if (notify is null)
        {
            CompatShimHost.Log($"{sTitle}: {sMessage}");
        }
        else
        {
            notify(sMessage, sTitle);
        }
    }

    public static void ReportException(Exception eX) => ReportException(eX, "Extension error");

    /// <summary>Records an exception an extension caught. Logged with its stack trace; it doesn't interrupt the user.</summary>
    public static void ReportException(Exception eX, string sTitle) =>
        CompatShimHost.Log($"{sTitle}: {eX}");
}

/// <summary>Fiddler's log. Everything goes to <see cref="CompatShimHost.Log"/>.</summary>
public sealed class Logger
{
    internal Logger()
    {
    }

    public void LogString(string sMsg) => CompatShimHost.Log(sMsg ?? string.Empty);

    /// <summary>
    /// <see cref="string.Format(string, object[])"/>, then log. Called with
    /// no arguments, the text is logged as-is, so a message that happens to
    /// contain braces (an exception's text, say) can't throw a
    /// <see cref="FormatException"/> from inside an extension's own error
    /// handling.
    /// </summary>
    public void LogFormat(string format, params object?[] args)
    {
        if (args is null || args.Length == 0)
        {
            LogString(format);
            return;
        }

        try
        {
            LogString(string.Format(CultureInfo.CurrentCulture, format, args));
        }
        catch (FormatException)
        {
            LogString(format + " " + string.Join(", ", args));
        }
    }
}

/// <summary>
/// Fiddler's documented <c>IFiddlerPreferences</c>, minus watchers
/// (<c>AddWatcher</c> hands back a Fiddler-specific watcher type; that can
/// be added when an extension needs it). CLeARINET's own
/// <see cref="IPreferenceStore"/> was deliberately given the same method
/// names, so this is a thin pass-through.
/// </summary>
public interface IFiddlerPreferences
{
    string? this[string sPrefName] { get; set; }

    bool GetBoolPref(string sPrefName, bool bDefault);

    int GetInt32Pref(string sPrefName, int iDefault);

    string GetStringPref(string sPrefName, string sDefault);

    void SetBoolPref(string sPrefName, bool bValue);

    void SetInt32Pref(string sPrefName, int iValue);

    void SetStringPref(string sPrefName, string sValue);

    void RemovePref(string sPrefName);
}

/// <summary>
/// Backs <see cref="FiddlerApplication.Prefs"/> with whatever
/// <see cref="CompatShimHost.Preferences"/> currently is -- looked up on
/// every call, so a host can set it after this type was first touched. In
/// the desktop app that's the same <c>preferences.json</c> the app itself
/// uses, with the same behavior on Windows and macOS (see the Preferences
/// Design doc).
/// </summary>
internal sealed class HostPreferences : IFiddlerPreferences
{
    private static IPreferenceStore Store => CompatShimHost.Preferences;

    public string? this[string sPrefName]
    {
        get => Store[sPrefName];
        set
        {
            if (value is null)
            {
                Store.RemovePref(sPrefName);
            }
            else
            {
                Store.SetStringPref(sPrefName, value);
            }
        }
    }

    public bool GetBoolPref(string sPrefName, bool bDefault) => Store.GetBoolPref(sPrefName, bDefault);

    public int GetInt32Pref(string sPrefName, int iDefault) => Store.GetInt32Pref(sPrefName, iDefault);

    public string GetStringPref(string sPrefName, string sDefault) => Store.GetStringPref(sPrefName, sDefault);

    public void SetBoolPref(string sPrefName, bool bValue) => Store.SetBoolPref(sPrefName, bValue);

    public void SetInt32Pref(string sPrefName, int iValue) => Store.SetInt32Pref(sPrefName, iValue);

    public void SetStringPref(string sPrefName, string sValue) => Store.SetStringPref(sPrefName, sValue);

    public void RemovePref(string sPrefName) => Store.RemovePref(sPrefName);
}
