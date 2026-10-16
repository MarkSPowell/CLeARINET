namespace Clearinet.DesktopUi.ViewModels;

/// <summary>What the main window's own tool windows (Compare Sessions, TextWizard) need from the view model.</summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>Shows <paramref name="text"/> on the status line.</summary>
    public void ShowStatus(string text) => StatusText = text;

    /// <summary>TextWizard's last conversion, by name ("" the first time).</summary>
    public string TextWizardTransform => _preferences.GetStringPref(PreferenceKeys.TextWizardTransform, string.Empty);

    /// <summary>TextWizard's last character encoding, by name ("" the first time).</summary>
    public string TextWizardEncoding => _preferences.GetStringPref(PreferenceKeys.TextWizardEncoding, string.Empty);

    public void RememberTextWizardChoice(string transform, string encoding) =>
        _preferences.SetPrefs(
        [
            new(PreferenceKeys.TextWizardTransform, transform),
            new(PreferenceKeys.TextWizardEncoding, encoding),
        ]);
}
