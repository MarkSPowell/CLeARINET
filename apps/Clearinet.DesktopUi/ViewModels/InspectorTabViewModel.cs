using Clearinet.Extensibility.Inspection;

namespace Clearinet.DesktopUi.ViewModels;

/// <summary>
/// One inspector tab's already-computed content, ready for display. Plain
/// data, not a live view model -- inspector content is recomputed fresh
/// whenever the selected session changes (see MainWindowViewModel), so
/// there's nothing here that needs to react to later changes on its own.
/// </summary>
public sealed record InspectorTabViewModel(string DisplayName, InspectorContent Content);
