using Avalonia.Controls;
using Avalonia.Threading;
using ShimMenuItem = Clearinet.CompatShim.MenuItem;
using ShimMenuItemCollection = Clearinet.CompatShim.MenuItemCollection;

namespace Clearinet.DesktopUi;

/// <summary>
/// Draws the menu items extensions add through
/// <c>FiddlerApplication.UI</c> (see <c>Clearinet.CompatShim.MenuItem</c>)
/// as Avalonia menu items, and keeps each in step with its extension's
/// item: text, enabled, visible, checked and sub-items. Choosing one raises
/// the extension's <c>Click</c> on the UI thread.
///
/// Checked items get a check mark icon rather than Avalonia's own
/// toggle behaviour, because a WinForms-style extension toggles
/// <c>Checked</c> itself in its click handler; letting Avalonia toggle it
/// too would undo that.
/// </summary>
internal static class ExtensionMenus
{
    public static Control Build(ShimMenuItem item)
    {
        if (item.IsSeparator)
        {
            return new Separator();
        }

        var menuItem = new MenuItem();
        Update(menuItem, item);
        Fill(menuItem, item.MenuItems);

        item.PropertyChanged += (_, _) => OnUiThread(() => Update(menuItem, item));
        item.MenuItems.CollectionChanged += (_, _) => OnUiThread(() => Fill(menuItem, item.MenuItems));
        menuItem.Click += (_, e) =>
        {
            // A submenu's own header just opens it; a child's click bubbles
            // up here too, but the child marks it handled first.
            if (item.IsParent || e.Handled)
            {
                return;
            }

            e.Handled = true;
            try
            {
                item.PerformClick();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Extension] Menu item '{item.Text}' threw: {ex.Message}");
            }
        };

        return menuItem;
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread: now if already there, otherwise queued.</summary>
    public static void OnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    /// <summary>WinForms marks the access key with <c>&amp;</c> (and a literal ampersand as <c>&amp;&amp;</c>); Avalonia uses <c>_</c>.</summary>
    internal static string ToAccessText(string text)
    {
        const string LiteralAmpersand = "\u0001";
        return text
            .Replace("&&", LiteralAmpersand, StringComparison.Ordinal)
            .Replace("_", "__", StringComparison.Ordinal)
            .Replace("&", "_", StringComparison.Ordinal)
            .Replace(LiteralAmpersand, "&", StringComparison.Ordinal);
    }

    private static void Update(MenuItem menuItem, ShimMenuItem item)
    {
        menuItem.Header = ToAccessText(item.Text);
        menuItem.IsEnabled = item.Enabled;
        menuItem.IsVisible = item.Visible;
        menuItem.Icon = item.Checked ? new TextBlock { Text = "✓" } : null;
    }

    private static void Fill(MenuItem menuItem, ShimMenuItemCollection items)
    {
        menuItem.Items.Clear();
        foreach (var child in items)
        {
            menuItem.Items.Add(Build(child));
        }
    }
}
