using System.ComponentModel;
using Avalonia.Threading;
using Clearinet.ProxyCore.AutoResponder;

namespace Clearinet.DesktopUi.ViewModels;

/// <summary>Where the main window was, and how big, for the next run.</summary>
public sealed record WindowPlacement(int X, int Y, int Width, int Height, bool Maximized);

/// <summary>
/// What's remembered between runs beyond the preferences in
/// <see cref="PreferenceKeys"/>: the AutoResponder rules (their own file,
/// see <see cref="AutoResponderRuleFile"/>) and the window's placement.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private static readonly TimeSpan AutoResponderSaveDelay = TimeSpan.FromMilliseconds(500);

    private string? _autoResponderRulesPath;
    private DispatcherTimer? _autoResponderSaveTimer;

    /// <summary>
    /// Loads the saved rules into the AutoResponder (each with its own
    /// enabled checkbox as it was). The AutoResponder itself stays off until
    /// switched on, as before.
    /// </summary>
    private void LoadAutoResponderRules()
    {
        try
        {
            _autoResponderRulesPath = AutoResponderRuleFile.DefaultPath;
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"[AutoResponder] Rules won't be saved: {ex.Message}");
            return;
        }

        var rules = AutoResponderRuleFile.Load(_autoResponderRulesPath, out var error);
        if (error is not null)
        {
            Console.WriteLine($"[AutoResponder] {error}");
            StatusText = error;
        }

        foreach (var rule in rules)
        {
            _autoResponderRules.Rules.Add(rule);
            AutoResponderRuleRows.Add(CreateAutoResponderRow(rule));
        }
    }

    /// <summary>A row for <paramref name="rule"/> whose edits are saved.</summary>
    private AutoResponderRuleViewModel CreateAutoResponderRow(AutoResponderRule rule)
    {
        var row = new AutoResponderRuleViewModel(rule);
        row.PropertyChanged += OnAutoResponderRowChanged;
        return row;
    }

    private void OnAutoResponderRowChanged(object? sender, PropertyChangedEventArgs e) => ScheduleAutoResponderSave();

    /// <summary>
    /// Saves the rules shortly after the last change, so typing a pattern
    /// doesn't write the file on every keystroke.
    /// </summary>
    private void ScheduleAutoResponderSave()
    {
        if (_autoResponderRulesPath is null)
        {
            return;
        }

        if (_autoResponderSaveTimer is null)
        {
            _autoResponderSaveTimer = new DispatcherTimer { Interval = AutoResponderSaveDelay };
            _autoResponderSaveTimer.Tick += (_, _) => SaveAutoResponderRulesNow();
        }

        _autoResponderSaveTimer.Stop();
        _autoResponderSaveTimer.Start();
    }

    /// <summary>Writes any pending change now (also called on shutdown).</summary>
    private void SaveAutoResponderRulesNow()
    {
        if (_autoResponderSaveTimer is not { IsEnabled: true } || _autoResponderRulesPath is null)
        {
            return; // nothing pending
        }

        _autoResponderSaveTimer.Stop();
        try
        {
            AutoResponderRuleFile.Save(_autoResponderRulesPath, _autoResponderRules.Rules.ToList());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[AutoResponder] Couldn't save the rules: {ex.Message}");
            StatusText = $"Couldn't save the AutoResponder rules: {ex.Message}";
        }
    }

    /// <summary>The window's saved placement, or null on first run.</summary>
    public WindowPlacement? LoadWindowPlacement()
    {
        var width = _preferences.GetInt32Pref(PreferenceKeys.WindowWidth, 0);
        var height = _preferences.GetInt32Pref(PreferenceKeys.WindowHeight, 0);
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        return new WindowPlacement(
            _preferences.GetInt32Pref(PreferenceKeys.WindowLeft, 0),
            _preferences.GetInt32Pref(PreferenceKeys.WindowTop, 0),
            width,
            height,
            _preferences.GetBoolPref(PreferenceKeys.WindowMaximized, false));
    }

    /// <summary>Remembers the window's placement (position in screen pixels, size in the window's own units).</summary>
    public void SaveWindowPlacement(WindowPlacement placement)
    {
        _preferences.SetPrefs(
        [
            new(PreferenceKeys.WindowLeft, placement.X.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(PreferenceKeys.WindowTop, placement.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(PreferenceKeys.WindowWidth, placement.Width.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(PreferenceKeys.WindowHeight, placement.Height.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(PreferenceKeys.WindowMaximized, placement.Maximized ? bool.TrueString : bool.FalseString),
        ]);
    }
}
