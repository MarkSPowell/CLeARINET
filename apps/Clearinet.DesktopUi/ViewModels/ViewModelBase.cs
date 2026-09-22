using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Clearinet.DesktopUi.ViewModels;

/// <summary>
/// Minimal <see cref="INotifyPropertyChanged"/> base. No MVVM toolkit
/// dependency on purpose -- this app has exactly one view model so far, and
/// pulling in a source-generator package for a handful of properties isn't
/// worth the extra moving part yet. Revisit once there's a second one.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
