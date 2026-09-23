using Avalonia.Controls;
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

    public MainWindow()
    {
        InitializeComponent();
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
