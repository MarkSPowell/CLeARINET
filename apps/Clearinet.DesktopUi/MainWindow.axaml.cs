using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Clearinet.DesktopUi.ViewModels;

namespace Clearinet.DesktopUi;

public partial class MainWindow : Window
{
    /// <summary>
    /// Filters the native Open dialog down to .saz files -- the same
    /// StorageProvider-based picker Avalonia recommends over the obsolete
    /// Window.OpenFileDialog it replaced.
    /// </summary>
    private static readonly FilePickerFileType SazFileType = new("Session Archive Zip (*.saz)")
    {
        Patterns = ["*.saz"],
    };

    /// <summary>
    /// Filters <see cref="BrowseFiddlerScriptButton_Click"/>'s own
    /// picker down to <c>*.js</c> -- real FiddlerScript's default,
    /// JScript.NET-flavored variant (see the FiddlerScript Compatibility
    /// Design doc's engine discussion). The Roslyn-scripting <c>*.cs</c>
    /// variant Telerik later added isn't implemented yet, so it isn't
    /// listed here either; <see cref="FilePickerFileTypes.All"/> is offered
    /// alongside it so nothing already-working (a differently-extensioned
    /// script someone's using today) becomes unreachable through the picker.
    /// </summary>
    private static readonly FilePickerFileType FiddlerScriptFileType = new("FiddlerScript (*.js)")
    {
        Patterns = ["*.js"],
    };

    /// <summary>
    /// Every <see cref="DataGridTextColumn"/> this window has added itself
    /// for a Phase D <c>[BindUIColumn]</c> -- kept so
    /// <see cref="RefreshScriptColumns"/> can remove exactly these on the
    /// next script (re)load without touching the seven built-in columns
    /// declared in <c>MainWindow.axaml</c> itself.
    /// </summary>
    private readonly List<DataGridColumn> _scriptColumns = [];

    private MainWindowViewModel? _viewModel;

    private bool _extensionTabsAdded;

    private bool _extensionMenusAttached;

    private readonly List<Control> _extensionTopMenus = [];

    private readonly List<Control> _extensionToolsItems = [];

    public MainWindow()
    {
        InitializeComponent();

        // DataContext is set from outside (see App.axaml.cs), after this
        // constructor returns -- DataContextChanged is what actually gets
        // a live MainWindowViewModel to subscribe PropertyChanged against.
        DataContextChanged += (_, _) => AttachViewModel();
    }

    /// <summary>
    /// The only place custom grid columns can be built at all: a script's
    /// own <c>[BindUIColumn]</c> methods (see
    /// <c>MainWindowViewModel.ScriptDirectives.UIColumns</c>) aren't known
    /// until a script loads, and <c>DataGrid.Columns</c> is a plain
    /// code-behind collection with no XAML-bindable <c>ItemsSource</c>
    /// equivalent of its own -- the same reason
    /// <see cref="OpenSazMenuItem_Click"/> already has to live here rather
    /// than in the view model. Re-subscribes rather than assuming this
    /// only ever runs once, since Avalonia can in principle reassign a
    /// window's DataContext more than once.
    /// </summary>
    private void AttachViewModel()
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainWindowViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            RefreshScriptColumns();
            AddExtensionTabs(_viewModel);
            AttachExtensionMenusAndColumns();
        }
    }

    /// <summary>
    /// Shows what extensions add through <c>FiddlerApplication.UI</c>, and
    /// keeps showing it as they add more: top-level menus (before Help),
    /// Tools menu items (after a separator), and session-list columns that
    /// show a session flag. Runs once.
    /// </summary>
    private void AttachExtensionMenusAndColumns()
    {
        if (_extensionMenusAttached)
        {
            return;
        }

        _extensionMenusAttached = true;
        var ui = Clearinet.CompatShim.FiddlerApplication.UI;

        RefreshExtensionMenus();
        ui.mnuMain.MenuItems.CollectionChanged += (_, _) => ExtensionMenus.OnUiThread(RefreshExtensionMenus);
        ui.mnuTools.MenuItems.CollectionChanged += (_, _) => ExtensionMenus.OnUiThread(RefreshExtensionMenus);

        foreach (var column in ui.lvSessions.Columns)
        {
            AddExtensionColumn(column);
        }

        ui.lvSessions.ColumnAdded += (_, column) => ExtensionMenus.OnUiThread(() => AddExtensionColumn(column));
    }

    private void RefreshExtensionMenus()
    {
        var ui = Clearinet.CompatShim.FiddlerApplication.UI;

        foreach (var control in _extensionTopMenus)
        {
            MainMenuBar.Items.Remove(control);
        }

        _extensionTopMenus.Clear();
        var insertAt = MainMenuBar.Items.IndexOf(HelpMenu);
        foreach (var item in ui.mnuMain.MenuItems)
        {
            var control = ExtensionMenus.Build(item);
            MainMenuBar.Items.Insert(insertAt++, control);
            _extensionTopMenus.Add(control);
        }

        foreach (var control in _extensionToolsItems)
        {
            ToolsMenu.Items.Remove(control);
        }

        _extensionToolsItems.Clear();
        if (ui.mnuTools.MenuItems.Count > 0)
        {
            var separator = new Separator();
            ToolsMenu.Items.Add(separator);
            _extensionToolsItems.Add(separator);
            foreach (var item in ui.mnuTools.MenuItems)
            {
                var control = ExtensionMenus.Build(item);
                ToolsMenu.Items.Add(control);
                _extensionToolsItems.Add(control);
            }
        }
    }

    /// <summary>
    /// A session-list column showing one session flag (<c>lvSessions.AddBoundColumn</c>),
    /// bound to <c>SessionRow.Flags</c>. Placed at the extension's requested
    /// position, or last if that's past the end.
    /// </summary>
    private void AddExtensionColumn(Clearinet.CompatShim.BoundColumn bound)
    {
        var column = new DataGridTextColumn
        {
            Header = bound.Title,
            Binding = new Binding($"Flags[{bound.FlagName}]"),
            Width = bound.Width > 0 ? new DataGridLength(bound.Width) : DataGridLength.Auto,
        };

        var columns = SessionsDataGrid.Columns;
        var index = bound.DisplayOrder < 0 || bound.DisplayOrder > columns.Count ? columns.Count : bound.DisplayOrder;
        columns.Insert(index, column);
    }

    /// <summary>
    /// Adds a tab after Inspectors for each view an extension supplied
    /// through <c>ExtensionUi.AddTab</c>. Extensions only add tabs while
    /// loading, before this window exists, so this runs once. A view that
    /// isn't an Avalonia <see cref="Control"/> gets a tab explaining that,
    /// rather than being dropped silently. The tab strip stays hidden while
    /// Inspectors is the only tab.
    /// </summary>
    private void AddExtensionTabs(MainWindowViewModel viewModel)
    {
        if (_extensionTabsAdded)
        {
            return;
        }

        _extensionTabsAdded = true;
        foreach (var tab in viewModel.ExtensionTabs)
        {
            var content = tab.View as Control ?? new TextBlock
            {
                Text = $"This extension's view is a {tab.View.GetType().FullName}, not an Avalonia control, so CLeARINET can't show it.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8),
            };
            DetailTabs.Items.Add(new TabItem { Header = tab.Title, Content = content });
        }

        DetailTabs.Classes.Set("single", DetailTabs.Items.Count <= 1);
    }

    /// <summary>
    /// <see cref="MainWindowViewModel.FiddlerScriptStatus"/> changing is the
    /// signal a script just (re)loaded -- see
    /// <see cref="MainWindowViewModel.ScriptDirectives"/>'s own remarks on
    /// why that property, specifically, is what this piggybacks on rather
    /// than a dedicated "columns changed" event.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.FiddlerScriptStatus))
        {
            RefreshScriptColumns();
        }
    }

    /// <summary>The Delete key removes the selected session, as Edit > Remove Selected Session does.</summary>
    private void SessionsDataGrid_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None &&
            _viewModel?.RemoveSelectedSessionCommand is { } remove && remove.CanExecute(null))
        {
            remove.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Tags each session row, as it's shown (rows are reused while
    /// scrolling, so this runs again for every reuse), with the classes the
    /// grid's styles in MainWindow.axaml key on: <c>ui-back</c>,
    /// <c>ui-fore</c>, <c>ui-bold</c>, <c>ui-italic</c>, <c>ui-strikeout</c>.
    /// </summary>
    private void SessionsDataGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        var style = (e.Row.DataContext as Models.SessionRow)?.Style ?? Models.SessionRowStyle.Plain;
        e.Row.Classes.Set("ui-back", style.Background is not null);
        e.Row.Classes.Set("ui-fore", style.Foreground is not null);
        e.Row.Classes.Set("ui-bold", style.Bold);
        e.Row.Classes.Set("ui-italic", style.Italic);
        e.Row.Classes.Set("ui-strikeout", style.Strikeout);
    }

    /// <summary>
    /// Rebuilds every <c>[BindUIColumn]</c>-provided <see cref="DataGridColumn"/>
    /// from scratch against whatever the currently-loaded script declares,
    /// appended after the seven built-in columns, in declaration order --
    /// <c>UIColumnDescriptor.DisplayOrder</c> is scanned but not honored
    /// here (see that record's own remarks). Each column's
    /// <see cref="DataGridTextColumn.Binding"/> is a classic indexer path
    /// (<c>ScriptColumns[Title]</c>) against <c>SessionRow.ScriptColumns</c>
    /// -- the same reflection-binding mechanism
    /// <c>x:CompileBindings="False"</c> already opts this whole DataGrid
    /// subtree into, for exactly this reason.
    /// </summary>
    private void RefreshScriptColumns()
    {
        if (_viewModel is null)
        {
            return;
        }

        foreach (var column in _scriptColumns)
        {
            SessionsDataGrid.Columns.Remove(column);
        }

        _scriptColumns.Clear();

        foreach (var descriptor in _viewModel.ScriptDirectives.UIColumns)
        {
            var column = new DataGridTextColumn
            {
                Header = descriptor.ColumnTitle,
                Binding = new Binding($"ScriptColumns[{descriptor.ColumnTitle}]"),
                Width = descriptor.Width is { } width ? new DataGridLength(width) : DataGridLength.Auto,
            };

            SessionsDataGrid.Columns.Add(column);
            _scriptColumns.Add(column);
        }
    }

    /// <summary>
    /// The only place this window's code-behind does real work, rather than
    /// leaving everything to bindings and commands: picking a file needs
    /// <see cref="TopLevel.StorageProvider"/>, which only a live control in
    /// the visual tree can reach, so it can't live in
    /// <see cref="MainWindowViewModel"/> the way every other command here
    /// does. <c>async void</c> is the only legal signature for an Avalonia
    /// event handler, but the actual import work is properly awaited
    /// (<see cref="MainWindowViewModel.ImportSazAsync"/>) rather than
    /// blocked on -- see that method's own remarks on why a synchronous
    /// wrapper would risk a UI-thread deadlock.
    /// </summary>
    private async void OpenSazMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var topLevel = GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open SAZ",
            AllowMultiple = false,
            FileTypeFilter = [SazFileType],
        });

        var file = files.Count > 0 ? files[0] : null;
        if (file is null)
        {
            return;
        }

        await viewModel.ImportSazAsync(file.Path.LocalPath);
    }

    /// <summary>
    /// Opens a native file picker for MainWindow.axaml's FiddlerScript path
    /// field, which is <c>IsReadOnly="True"</c> so this "Browse…" button is
    /// the only way that field's text ever changes. Originally wired as a
    /// <c>PointerPressed</c> handler directly on the <c>TextBox</c> itself
    /// (clicking anywhere in the field, no separate button needed), which
    /// turned out not to fire: an Avalonia <see cref="TextBox"/>, even
    /// read-only, still handles pointer-press internally for caret
    /// placement and text selection, and marks the event
    /// <see cref="Avalonia.Input.PointerEventArgs.Handled"/> before it ever
    /// bubbles out to a plain XAML <c>PointerPressed</c> attribute -- not
    /// something this sandbox could catch up front with no build/click-test
    /// loop of its own. Same pattern and reasoning as
    /// <see cref="OpenSazMenuItem_Click"/> otherwise (StorageProvider only
    /// reachable from a live control, <c>async void</c> the only legal
    /// event-handler signature, a plain <see cref="Button.Click"/> proven to
    /// actually fire where <c>PointerPressed</c> didn't). Canceling the
    /// picker leaves <see cref="MainWindowViewModel.FiddlerScriptPath"/>
    /// exactly as it was, the same as canceling "Open SAZ" leaves nothing
    /// changed.
    /// </summary>
    private async void BrowseFiddlerScriptButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var topLevel = GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a FiddlerScript file",
            AllowMultiple = false,
            FileTypeFilter = [FiddlerScriptFileType, FilePickerFileTypes.All],
        });

        var file = files.Count > 0 ? files[0] : null;
        if (file is null)
        {
            return;
        }

        viewModel.FiddlerScriptPath = file.Path.LocalPath;
    }

    /// <summary>
    /// Help -&gt; About CLeARINET. Needs this window as the new dialog's
    /// owner (<c>ShowDialog(this)</c>, what actually centers it over
    /// MainWindow and blocks input to it while open) so, like
    /// <see cref="OpenSazMenuItem_Click"/> and
    /// <see cref="BrowseFiddlerScriptButton_Click"/> above, this lives here
    /// rather than as a <c>RelayCommand</c> on <see cref="MainWindowViewModel"/>.
    /// The result of <see cref="Window.ShowDialog(Window)"/> (a
    /// <see cref="Task"/> that completes when the dialog closes) is
    /// deliberately discarded, not awaited: nothing here needs to run after
    /// the About dialog closes, the same reasoning
    /// <c>App.axaml.cs</c>'s own fire-and-forget splash handoff already
    /// documents.
    /// </summary>
    private void AboutMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        var about = new AboutWindow();
        _ = about.ShowDialog(this);
    }
}
