using Violens.Extensibility.Inspection;

namespace Violens.DesktopUi.ViewModels;

/// <summary>One inspector tab's computed content.</summary>
public sealed record InspectorTabViewModel(string DisplayName, InspectorContent Content);
