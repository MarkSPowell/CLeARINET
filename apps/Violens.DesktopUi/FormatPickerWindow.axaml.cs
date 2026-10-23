using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Violens.Compatibility.Extensions;

namespace Violens.DesktopUi;

/// <summary>
/// Asks which format Import/Export via Extension should use when there's
/// more than one. Closes with the choice, or null if cancelled.
/// </summary>
public partial class FormatPickerWindow : Window
{
    /// <summary>For the XAML designer only.</summary>
    public FormatPickerWindow()
        : this("Choose a format", [], null)
    {
    }

    /// <param name="heading">The question, e.g. "Import sessions using which format?".</param>
    /// <param name="choices">What to offer, from <see cref="ProfferedFormats.ChoicesFrom"/>.</param>
    /// <param name="rememberedKey">The last choice's <see cref="FormatChoice.Key"/>, highlighted if it's still offered.</param>
    public FormatPickerWindow(string heading, IReadOnlyList<FormatChoice> choices, string? rememberedKey)
    {
        InitializeComponent();

        HeadingText.Text = heading;
        ChoicesList.ItemsSource = choices;
        ChoicesList.SelectedItem = ProfferedFormats.Preselect(choices, rememberedKey);
        ContinueButton.IsEnabled = ChoicesList.SelectedItem is not null;
    }

    private void ChoicesList_SelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        ContinueButton.IsEnabled = ChoicesList.SelectedItem is not null;

    private void ChoicesList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ChoicesList.SelectedItem is FormatChoice choice)
        {
            Close(choice);
        }
    }

    private void ContinueButton_Click(object? sender, RoutedEventArgs e) => Close(ChoicesList.SelectedItem as FormatChoice);

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(null);
}
