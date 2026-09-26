using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Clearinet.CompatShim;

/// <summary>
/// A menu item an extension creates and adds to one of the host's menus
/// (<see cref="frmViewer.mnuMain"/>, <see cref="frmViewer.mnuTools"/>).
/// Shaped like the WinForms <c>MenuItem</c> Fiddler Classic extensions use,
/// so their menu code compiles unchanged once <c>System.Windows.Forms.</c>
/// is dropped from the type name. It isn't a control: CLeARINET's window
/// watches it (<see cref="INotifyPropertyChanged"/>) and draws a matching
/// Avalonia menu item, on Windows and macOS.
///
/// <para><see cref="Text"/> uses the WinForms convention: <c>&amp;</c>
/// marks the access key and <c>&amp;&amp;</c> is a literal ampersand. A
/// <see cref="Text"/> of <c>-</c> is a separator. Clicking the host's item
/// raises <see cref="Click"/> on the UI thread; as in WinForms, it doesn't
/// toggle <see cref="Checked"/> by itself.</para>
/// </summary>
public class MenuItem : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private bool _checked;
    private bool _enabled = true;
    private bool _visible = true;

    public MenuItem()
    {
        MenuItems = new MenuItemCollection(this);
    }

    public MenuItem(string text)
        : this()
    {
        _text = text ?? string.Empty;
    }

    public MenuItem(string text, EventHandler onClick)
        : this(text)
    {
        if (onClick is not null)
        {
            Click += onClick;
        }
    }

    public MenuItem(string text, MenuItem[] items)
        : this(text)
    {
        MenuItems.AddRange(items);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when the user chooses this item (or <see cref="PerformClick"/> is called).</summary>
    public event EventHandler? Click;

    public string Text
    {
        get => _text;
        set => Set(ref _text, value ?? string.Empty, nameof(Text));
    }

    public bool Checked
    {
        get => _checked;
        set => Set(ref _checked, value, nameof(Checked));
    }

    public bool Enabled
    {
        get => _enabled;
        set => Set(ref _enabled, value, nameof(Enabled));
    }

    public bool Visible
    {
        get => _visible;
        set => Set(ref _visible, value, nameof(Visible));
    }

    /// <summary>Kept for source compatibility; items are shown in the order they were added.</summary>
    public int Index { get; set; }

    /// <summary>Kept for source compatibility; a checked item is always drawn with a check mark.</summary>
    public bool RadioCheck { get; set; }

    /// <summary>Any value the extension wants to keep with the item.</summary>
    public object? Tag { get; set; }

    /// <summary>Sub-items. An item with any is shown as a submenu.</summary>
    public MenuItemCollection MenuItems { get; }

    /// <summary>The item this one was added to, if any.</summary>
    public MenuItem? Parent { get; internal set; }

    public bool IsParent => MenuItems.Count > 0;

    public bool IsSeparator => _text == "-";

    /// <summary>Raises <see cref="Click"/>, as if the user chose the item.</summary>
    public void PerformClick() => Click?.Invoke(this, EventArgs.Empty);

    public override string ToString() => Text;

    private void Set<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// A menu's items, shaped like WinForms' <c>Menu.MenuItemCollection</c>
/// (<c>Add</c> returns the new item's index; <c>AddRange</c>). Raises
/// <see cref="INotifyCollectionChanged.CollectionChanged"/> so the host can
/// keep its drawn menu in step.
/// </summary>
public sealed class MenuItemCollection : IList<MenuItem>, INotifyCollectionChanged
{
    private readonly List<MenuItem> _items = [];
    private readonly MenuItem? _owner;

    internal MenuItemCollection(MenuItem? owner) => _owner = owner;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public int Count => _items.Count;

    public bool IsReadOnly => false;

    public MenuItem this[int index]
    {
        get => _items[index];
        set
        {
            var old = _items[index];
            _items[index] = Adopt(value);
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, value, old, index));
        }
    }

    /// <summary>Adds <paramref name="item"/> at the end and returns its index.</summary>
    public int Add(MenuItem item)
    {
        Insert(_items.Count, item);
        return _items.Count - 1;
    }

    /// <summary>Adds a new item with this text and returns it.</summary>
    public MenuItem Add(string caption)
    {
        var item = new MenuItem(caption);
        Add(item);
        return item;
    }

    /// <summary>Adds a new item with this text and click handler and returns it.</summary>
    public MenuItem Add(string caption, EventHandler onClick)
    {
        var item = new MenuItem(caption, onClick);
        Add(item);
        return item;
    }

    /// <summary>Adds a new item with this text and sub-items and returns it.</summary>
    public MenuItem Add(string caption, MenuItem[] items)
    {
        var item = new MenuItem(caption, items);
        Add(item);
        return item;
    }

    public void AddRange(MenuItem[] items)
    {
        foreach (var item in items ?? [])
        {
            Add(item);
        }
    }

    public void Insert(int index, MenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Insert(index, Adopt(item));
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
    }

    public bool Remove(MenuItem item)
    {
        var index = _items.IndexOf(item);
        if (index < 0)
        {
            return false;
        }

        RemoveAt(index);
        return true;
    }

    public void RemoveAt(int index)
    {
        var item = _items[index];
        _items.RemoveAt(index);
        item.Parent = null;
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));
    }

    public void Clear()
    {
        foreach (var item in _items)
        {
            item.Parent = null;
        }

        _items.Clear();
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    void ICollection<MenuItem>.Add(MenuItem item) => Add(item);

    public bool Contains(MenuItem item) => _items.Contains(item);

    public int IndexOf(MenuItem item) => _items.IndexOf(item);

    public void CopyTo(MenuItem[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    public IEnumerator<MenuItem> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private MenuItem Adopt(MenuItem item)
    {
        item.Parent = _owner;
        return item;
    }
}

/// <summary>A top-level menu bar, like WinForms' <c>MainMenu</c>: just its items.</summary>
public sealed class MainMenu
{
    public MenuItemCollection MenuItems { get; } = new(null);
}

/// <summary>A session-list column an extension added, showing one session flag.</summary>
/// <param name="Title">The column header.</param>
/// <param name="DisplayOrder">Where it goes: 0 is the first column; a negative value, or one past the end, puts it last.</param>
/// <param name="Width">Width in pixels; 0 or less means automatic.</param>
/// <param name="FlagName">The session flag whose value the column shows (case-insensitive).</param>
public sealed record BoundColumn(string Title, int DisplayOrder, int Width, string FlagName);

/// <summary>
/// The session list, as far as extensions can change it: adding columns
/// that show a session flag, like Fiddler Classic's
/// <c>lvSessions.AddBoundColumn</c>. The host watches <see cref="Columns"/>
/// and adds a matching column to its grid.
/// </summary>
public sealed class SessionListView
{
    private readonly object _gate = new();
    private readonly List<BoundColumn> _columns = [];

    /// <summary>Raised (on the thread that added it) when a column is added.</summary>
    public event EventHandler<BoundColumn>? ColumnAdded;

    public IReadOnlyList<BoundColumn> Columns
    {
        get
        {
            lock (_gate)
            {
                return [.. _columns];
            }
        }
    }

    /// <summary>
    /// Adds a column titled <paramref name="sColumnTitle"/> showing each
    /// session's <paramref name="sSessionFlagName"/> flag. Returns false,
    /// adding nothing, if a column with that title already exists.
    /// </summary>
    public bool AddBoundColumn(string sColumnTitle, int iDisplayOrder, int iWidth, string sSessionFlagName)
    {
        if (string.IsNullOrWhiteSpace(sColumnTitle) || string.IsNullOrWhiteSpace(sSessionFlagName))
        {
            return false;
        }

        var column = new BoundColumn(sColumnTitle, iDisplayOrder, iWidth, sSessionFlagName);
        lock (_gate)
        {
            if (_columns.Any(c => string.Equals(c.Title, sColumnTitle, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            _columns.Add(column);
        }

        ColumnAdded?.Invoke(this, column);
        return true;
    }

    /// <summary>As <see cref="AddBoundColumn(string, int, int, string)"/>, placing the column last.</summary>
    public bool AddBoundColumn(string sColumnTitle, int iWidth, string sSessionFlagName) =>
        AddBoundColumn(sColumnTitle, -1, iWidth, sSessionFlagName);
}

/// <summary>
/// The parts of the host's main window an extension can extend:
/// <see cref="FiddlerApplication.UI"/>. Named, and its members named, as in
/// Fiddler Classic, so extension code compiles unchanged:
/// <list type="bullet">
/// <item><see cref="mnuMain"/>: add a top-level menu (shown before Help).</item>
/// <item><see cref="mnuTools"/>: add items to the Tools menu.</item>
/// <item><see cref="lvSessions"/>: add session-list columns.</item>
/// </list>
/// For a whole tab, use <c>Clearinet.Compatibility.Extensions.ExtensionUi.AddTab</c>.
/// </summary>
public sealed class frmViewer
{
    internal frmViewer()
    {
    }

    public MainMenu mnuMain { get; } = new();

    public MenuItem mnuTools { get; } = new("&Tools");

    public SessionListView lvSessions { get; } = new();
}
