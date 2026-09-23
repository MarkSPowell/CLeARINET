using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
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
    /// Every <see cref="DataGridTextColumn"/> this window has added itself
    /// for a Phase D <c>[BindUIColumn]</c> -- kept so
    /// <see cref="RefreshScriptColumns"/> can remove exactly these on the
    /// next script (re)load without touching the seven built-in columns
    /// declared in <c>MainWindow.axaml</c> itself.
    /// </summary>
    private readonly List<DataGridColumn> _scriptColumns = [];

    private MainWindowViewModel? _viewModel;

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
        }
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
}
