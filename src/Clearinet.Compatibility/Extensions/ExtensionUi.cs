namespace Clearinet.Compatibility.Extensions;

/// <summary>
/// How an extension adds its own UI to CLeARINET. This is CLeARINET's API,
/// not a Fiddler one: Fiddler Classic's equivalents
/// (<c>FiddlerApplication.UI.tabsViews</c>, <c>mnuMain</c>) are WinForms
/// controls, which don't exist on macOS or inside an Avalonia app. A ported
/// extension calls this in place of its WinForms code, usually behind
/// <c>#if CLEARINET</c> so the same source still builds for Fiddler Classic
/// (see the Extension Test Targets doc).
///
/// Views are Avalonia controls. This assembly doesn't reference Avalonia,
/// so they're passed as <see cref="object"/>; the app rejects (and logs)
/// anything that isn't an Avalonia <c>Control</c>. An extension that adds a
/// view references the Avalonia package at compile time only and uses the
/// app's own copy at run time, so the versions always match.
///
/// Call it from <c>OnLoad</c>, which runs on the UI thread. Tabs added
/// before the app's window exists are kept and shown once it does.
/// </summary>
public static class ExtensionUi
{
    private static readonly object Gate = new();
    private static readonly List<(string Title, object View)> Pending = [];
    private static Action<string, object>? s_tabHost;

    /// <summary>
    /// Adds a tab titled <paramref name="title"/> showing
    /// <paramref name="view"/> (an Avalonia <c>Control</c>) beside the
    /// Inspectors, the way Fiddler Classic extensions add a tab to its main
    /// tab strip.
    /// </summary>
    public static void AddTab(string title, object view)
    {
        ArgumentNullException.ThrowIfNull(view);
        title = string.IsNullOrWhiteSpace(title) ? "Extension" : title;

        Action<string, object>? host;
        lock (Gate)
        {
            host = s_tabHost;
            if (host is null)
            {
                Pending.Add((title, view));
                return;
            }
        }

        host(title, view);
    }

    /// <summary>
    /// Called by the app to receive tabs: any added so far are passed on
    /// immediately, in order. Pass null when the app shuts down.
    /// </summary>
    public static void SetTabHost(Action<string, object>? tabHost)
    {
        List<(string Title, object View)> flush;
        lock (Gate)
        {
            s_tabHost = tabHost;
            if (tabHost is null)
            {
                return;
            }

            flush = [.. Pending];
            Pending.Clear();
        }

        foreach (var (title, view) in flush)
        {
            tabHost(title, view);
        }
    }
}
