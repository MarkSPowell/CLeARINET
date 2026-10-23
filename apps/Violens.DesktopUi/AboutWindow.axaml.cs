using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Violens.DesktopUi;

/// <summary>Help > About VioLens: shows which build is running.</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        // InformationalVersion, because the numeric assembly and file versions
        // can't carry a "-preview.1" suffix.
        var informationalVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "unknown";

        // Drop any "+metadata" so the version matches the git tag.
        VersionText.Text = $"Version {informationalVersion.Split('+')[0]}";
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
