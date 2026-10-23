using System.Windows.Forms;

namespace Violens.CompatShim;

/// <summary><c>GetUserString(string, string, string, bool)</c>, used by ContentBlock and SAZClipboard.</summary>
public static class frmPrompt
{
    /// <summary>
    /// Shows a small input dialog. The meaning of the last argument isn't
    /// known: both callers pass true, one for a host list and one for a
    /// password, so it can't be "mask input". It's kept for compatibility and
    /// ignored, and input is never masked.
    /// </summary>
    public static string GetUserString(string title, string prompt, string defaultValue, bool unconfirmedFlag)
    {
        _ = unconfirmedFlag; // Deliberately unused -- see the remarks above.

        using var dialog = new Form
        {
            Text = title ?? "Input",
            Width = 420,
            Height = 160,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MinimizeBox = false,
            MaximizeBox = false,
        };

        var label = new Label { Text = prompt ?? string.Empty, Left = 12, Top = 12, Width = 380, Height = 40 };
        var textBox = new TextBox
        {
            Text = defaultValue ?? string.Empty,
            Left = 12,
            Top = 56,
            Width = 380,
        };
        var okButton = new Button { Text = "OK", Left = 232, Top = 88, Width = 75, DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "Cancel", Left = 317, Top = 88, Width = 75, DialogResult = DialogResult.Cancel };

        dialog.Controls.Add(label);
        dialog.Controls.Add(textBox);
        dialog.Controls.Add(okButton);
        dialog.Controls.Add(cancelButton);
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        return dialog.ShowDialog() == DialogResult.OK ? textBox.Text : null;
    }
}
