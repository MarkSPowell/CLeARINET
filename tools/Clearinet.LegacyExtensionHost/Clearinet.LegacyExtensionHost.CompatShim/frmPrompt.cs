using System.Windows.Forms;

namespace Clearinet.CompatShim;

/// <summary>
/// metadata: <c>Fiddler.frmPrompt.static string GetUserString(string, string, string, bool)</c>
/// -- ContentBlock, SAZClipboard. A much smaller, self-contained surface
/// than <see cref="Fiddler.frmViewer"/> -- see the design doc's "a smaller,
/// real win" option: no shared-control-tree dependency, just a static
/// method backed by a real, small input dialog.
/// </summary>
public static class frmPrompt
{
    /// <summary>
    /// Parameter order (title, prompt, default value, then a trailing
    /// <see langword="bool"/>) is confirmed against two real call sites now,
    /// not just guessed from the signature's types -- but the trailing
    /// bool's own MEANING is confirmed to NOT be "mask input", which is
    /// what this parameter (formerly named <c>maskInput</c>) originally
    /// guessed. Real IL from both extensions that call this
    /// (<c>ildasm</c>'d directly off their own compiled `.dll`s, same
    /// clean-room "read the caller's own metadata/IL" approach as
    /// everywhere else in this shim -- see the design doc's "Don't get
    /// sued" section):
    /// <list type="bullet">
    /// <item>ContentBlocker's "Edit Blocked Host List" prompt (editing a
    /// semicolon-delimited list, pre-filled from the existing list) passes
    /// <c>ldc.i4.1</c> (<see langword="true"/>) for this parameter.</item>
    /// <item>SAZClipboard's "Password-Protect SAZ (AES Encryption)" prompt
    /// (asking for a password to encrypt a session archive) ALSO passes
    /// <c>ldc.i4.1</c> (<see langword="true"/>) for this same
    /// parameter.</item>
    /// </list>
    /// The same value can't mean "mask this" for one call and "don't mask
    /// this" for the other -- a masked host-list editor is a confirmed,
    /// directly observed bug (see this project's own README/design doc
    /// notes on it), so "mask input" is ruled out as this parameter's
    /// meaning, whatever it actually is (multiline and "select all on
    /// open" were both considered; neither fits a single-line password
    /// prompt either). Rather than keep guessing, this dialog now never
    /// masks: a visible password character is a minor privacy nicety lost
    /// for SAZClipboard's prompt, not a functional bug, versus the
    /// actively-broken masked host-list editor this shipped with before.
    /// Still not confirmed what the parameter actually does -- kept in the
    /// signature (removing it would break binary compatibility with both
    /// callers) but deliberately unused, and renamed from the
    /// now-disproven <c>maskInput</c> to <paramref name="unconfirmedFlag"/>
    /// so nothing in this codebase implies more confidence than the
    /// evidence supports.
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
