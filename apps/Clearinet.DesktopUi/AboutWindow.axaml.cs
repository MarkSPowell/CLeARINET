using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Clearinet.DesktopUi;

/// <summary>
/// Help -> About CLeARINET (see MainWindow.axaml's Help menu and
/// MainWindow.axaml.cs's AboutMenuItem_Click): a small, non-resizable
/// modal whose only real job is reporting which build is actually
/// running. Constructed and shown fresh on every click rather than kept
/// as a long-lived singleton -- this dialog has no state worth preserving
/// between opens, so there's nothing a reused instance would save.
/// </summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        // AssemblyInformationalVersionAttribute specifically, not
        // Assembly.GetName().Version or AssemblyFileVersionAttribute --
        // both of those are the numeric-only four-part Windows version
        // (major.minor.build.revision) and can't carry a prerelease
        // suffix like "-preview.1" at all, so a real preview build would
        // show up here as plain "0.1.0", silently dropping exactly the
        // part of the version that says it's a preview.
        // InformationalVersion is the one MSBuild property that keeps the
        // full string -p:Version=... was given, whether that's this
        // project's own "0.0.0-dev" default (see
        // Clearinet.DesktopUi.csproj's own remarks) or a real tag stamped
        // in by .github/workflows/release-windows.yml.
        var informationalVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "unknown";

        // Split off anything after a "+": InformationalVersion can carry
        // build metadata (most commonly a source-control revision) after
        // a "+", which nothing in this project's build adds today, but
        // showing only the semantic version proper here -- not metadata
        // that might get appended by some future change to the build --
        // is what actually matches the git tag someone would go look for.
        VersionText.Text = $"Version {informationalVersion.Split('+')[0]}";
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
}
