using System.Windows.Input;

namespace Clearinet.DesktopUi.ViewModels;

/// <summary>
/// The standard hand-rolled <see cref="ICommand"/> every WPF/Avalonia MVVM
/// codebase reaches for -- no dependency worth adding just for this.
/// </summary>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => execute();

    /// <summary>
    /// Call after something the <c>canExecute</c> check depends on changes
    /// -- there's no automatic tracking here, unlike a toolkit's
    /// <c>[NotifyCanExecuteChangedFor]</c>.
    /// </summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
