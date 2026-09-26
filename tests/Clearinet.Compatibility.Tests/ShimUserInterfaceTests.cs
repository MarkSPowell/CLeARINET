using System.Collections.Specialized;
using Clearinet.CompatShim;
using Xunit;

namespace Clearinet.Compatibility.Tests;

/// <summary>
/// The Fiddler-shaped UI stand-ins extensions use through
/// <see cref="FiddlerApplication.UI"/>: WinForms-style menu items and
/// flag-bound session-list columns. The app's drawing of them is Avalonia
/// UI and isn't covered here.
/// </summary>
public class ShimUserInterfaceTests
{
    [Fact]
    public void MenuItemsBehaveLikeWinForms()
    {
        var clicks = 0;
        var parent = new MenuItem("&Privacy");
        var enabled = new MenuItem("&Enabled", (_, _) => clicks++);
        var other = new MenuItem("Other");

        Assert.Equal(0, parent.MenuItems.Add(enabled));
        parent.MenuItems.AddRange([other]);
        var added = parent.MenuItems.Add("Third");

        Assert.Equal(new[] { "&Enabled", "Other", "Third" }, parent.MenuItems.Select(i => i.Text).ToArray());
        Assert.Same(parent, added.Parent);
        Assert.True(parent.IsParent);
        Assert.True(enabled.Enabled);
        Assert.False(enabled.Checked);

        enabled.PerformClick();
        Assert.Equal(1, clicks);
        Assert.False(enabled.Checked); // as in WinForms, a click doesn't toggle Checked by itself
    }

    [Fact]
    public void ChangesAreObservableSoTheAppCanRedraw()
    {
        var item = new MenuItem("Item");
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var collectionChanges = new List<NotifyCollectionChangedAction>();
        item.MenuItems.CollectionChanged += (_, e) => collectionChanges.Add(e.Action);

        item.Checked = true;
        item.Checked = true; // unchanged: no event
        item.Enabled = false;
        item.Text = "Renamed";
        item.MenuItems.Add(new MenuItem("Child"));
        item.MenuItems.RemoveAt(0);

        Assert.Equal(new[] { nameof(MenuItem.Checked), nameof(MenuItem.Enabled), nameof(MenuItem.Text) }, changed.ToArray());
        Assert.Equal(new[] { NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Remove }, collectionChanges.ToArray());
    }

    [Fact]
    public void ADashIsASeparator()
    {
        Assert.True(new MenuItem("-").IsSeparator);
        Assert.False(new MenuItem("A - B").IsSeparator);
    }

    [Fact]
    public void BoundColumnsAreAddedOncePerTitle()
    {
        var list = new SessionListView();
        var raised = new List<BoundColumn>();
        list.ColumnAdded += (_, column) => raised.Add(column);

        Assert.True(list.AddBoundColumn("Privacy Info", 1, 120, "X-Privacy"));
        Assert.False(list.AddBoundColumn("privacy info", 2, 80, "Other"));
        Assert.True(list.AddBoundColumn("Last", 50, "x-last"));

        Assert.Equal(new[] { "Privacy Info", "Last" }, list.Columns.Select(c => c.Title).ToArray());
        Assert.Equal(new BoundColumn("Privacy Info", 1, 120, "X-Privacy"), list.Columns[0]);
        Assert.Equal(-1, list.Columns[1].DisplayOrder);
        Assert.Equal(2, raised.Count);
    }
}
