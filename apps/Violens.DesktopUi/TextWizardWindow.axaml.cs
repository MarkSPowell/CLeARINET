using Avalonia.Controls;
using Avalonia.Interactivity;
using Violens.AppCore.Text;

namespace Violens.DesktopUi;

/// <summary>
/// TextWizard: converts the input with the chosen <see cref="TextTransform"/>
/// as you type. One window, kept open while the app runs; opening it again
/// (Ctrl+E) brings it forward, with any text selected in the main window
/// as the new input.
/// </summary>
public partial class TextWizardWindow : Window
{
    private readonly Action<string, string>? _rememberChoice;
    private bool _loading = true;

    public TextWizardWindow()
        : this(null, null, null)
    {
    }

    /// <param name="transformName">The conversion to start on (the last one used), if any.</param>
    /// <param name="encodingName">The encoding to start on, if any.</param>
    /// <param name="rememberChoice">Called with the conversion and encoding names when either changes.</param>
    public TextWizardWindow(string? transformName, string? encodingName, Action<string, string>? rememberChoice)
    {
        InitializeComponent();
        _rememberChoice = rememberChoice;

        TransformBox.ItemsSource = TextTransforms.All;
        TransformBox.SelectedItem = TextTransforms.All.FirstOrDefault(t => t.Name == transformName)
                                    ?? TextTransforms.All.First(t => t.Name == "From Base64");
        EncodingBox.ItemsSource = TextTransforms.Encodings;
        EncodingBox.SelectedItem = TextTransforms.Encodings.FirstOrDefault(e => e.Name == encodingName)
                                   ?? TextTransforms.Encodings[0];
        _loading = false;
        ConvertInput();
    }

    /// <summary>Puts <paramref name="text"/> in the input (and so converts it).</summary>
    public void SetInput(string text)
    {
        InputBox.Text = text;
        InputBox.Focus();
    }

    private void SettingChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (TransformBox.SelectedItem is TextTransform transform && EncodingBox.SelectedItem is TextEncodingChoice encoding)
        {
            _rememberChoice?.Invoke(transform.Name, encoding.Name);
        }

        ConvertInput();
    }

    private void InputBox_TextChanged(object? sender, TextChangedEventArgs e) => ConvertInput();

    private void UseOutputButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(OutputBox.Text))
        {
            SetInput(OutputBox.Text);
        }
    }

    private void ConvertInput()
    {
        if (_loading || TransformBox.SelectedItem is not TextTransform transform ||
            EncodingBox.SelectedItem is not TextEncodingChoice encoding)
        {
            return;
        }

        DescriptionText.Text = transform.Description;
        EncodingBox.IsEnabled = transform.UsesEncoding;
        EncodingLabel.Opacity = transform.UsesEncoding ? 1 : 0.5;

        var input = InputBox.Text ?? string.Empty;
        TextTransformResult result;
        try
        {
            result = transform.Apply(input, encoding.Encoding);
        }
        catch (Exception ex)
        {
            // A conversion should report problems itself; this is a backstop.
            result = TextTransformResult.Error($"Couldn't convert that: {ex.Message}");
        }

        OutputBox.Text = result.Output;
        NoteText.Text = input.Length == 0 ? string.Empty : result.Note ?? string.Empty;
        NoteText.Foreground = this.FindResource(result.Failed ? "AppErrorBrush" : "AppMutedTextBrush") as Avalonia.Media.IBrush;
    }
}
