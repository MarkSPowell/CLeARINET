namespace Violens.Extensibility.Inspection;

/// <summary>
/// A session inspector: turns one side of a session into
/// <see cref="InspectorContent"/> for the host to render. Unlike Fiddler's
/// <c>Inspector2</c>, which returned a WinForms control, an inspector never
/// touches a UI toolkit, so any front end can show it and it can load in an
/// isolated <c>AssemblyLoadContext</c>.
/// </summary>
public interface IInspector
{
    /// <summary>A stable id, never shown; <c>"violens.&lt;name&gt;"</c> for built-ins.</summary>
    string Id { get; }

    /// <summary>The tab label, matching Fiddler Classic's names ("Headers", "Raw", "Hex") where there's one.</summary>
    string DisplayName { get; }

    /// <summary>Tab order: lower first, ties by <see cref="DisplayName"/>.</summary>
    int SortOrder { get; }

    /// <summary>Whether this inspector has anything to show for <paramref name="context"/>, so the host knows which tabs to offer.</summary>
    bool CanInspect(InspectorContext context);

    /// <summary>
    /// The content for <paramref name="context"/>, after <see cref="CanInspect"/>
    /// returned true. Return <see cref="ErrorContent"/> for bad input rather
    /// than throwing.
    /// </summary>
    InspectorContent Inspect(InspectorContext context);
}
