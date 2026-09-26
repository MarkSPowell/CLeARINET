using Clearinet.Extensibility.Inspection.Inspectors;

namespace Clearinet.Extensibility.Inspection;

/// <summary>
/// Holds the inspectors available to inspect with and picks the ones
/// applicable to a given context, in display order.
///
/// Only first-party, in-process inspectors for now. Loading third-party
/// inspector plugins from an Inspectors folder into isolated
/// <c>AssemblyLoadContext</c>s -- the way Fiddler Classic's "Extension
/// loading" worked -- is Beta-tier (see the Fiddler Feature Inventory
/// doc's Extensibility table and the project plan's Milestones), not part
/// of this MVP slice. <see cref="IInspector"/> itself is written so that
/// extending this registry to also discover and load external assemblies
/// later doesn't require changing the interface -- only this class.
/// </summary>
public sealed class InspectorRegistry
{
    private readonly List<IInspector> _inspectors;

    public InspectorRegistry(IEnumerable<IInspector> inspectors)
    {
        _inspectors = [.. inspectors];
    }

    public static InspectorRegistry CreateDefault() => CreateDefault(additional: null);

    /// <summary>
    /// Same three built-ins as the parameterless overload, plus whatever
    /// <paramref name="additional"/> inspectors a caller has on hand --
    /// exactly the seam this class's own remarks anticipated for loading
    /// third-party inspectors later. Used by <c>Clearinet.DesktopUi.ViewModels.MainWindowViewModel</c>
    /// to fold in <c>Clearinet.Compatibility.Extensions.Inspector2Adapter</c>-wrapped
    /// compiled-extension inspectors, discovered separately by
    /// <c>Clearinet.Compatibility.Extensions.ExtensionHost</c> -- this class
    /// itself stays unaware of extensions, .NET assemblies, or
    /// <c>AssemblyLoadContext</c> entirely, only of <see cref="IInspector"/>.
    /// </summary>
    public static InspectorRegistry CreateDefault(IEnumerable<IInspector>? additional)
    {
        IInspector[] builtins =
        [
            new HeadersInspector(),
            new RawTextInspector(),
            new HexInspector(),
            new CookiesInspector(),
            new NotesInspector(),
        ];

        return new InspectorRegistry(additional is null ? builtins : builtins.Concat(additional));
    }

    /// <summary>
    /// The inspectors that apply to <paramref name="context"/>, sorted by
    /// <see cref="IInspector.SortOrder"/> then <see cref="IInspector.DisplayName"/>.
    /// </summary>
    public IReadOnlyList<IInspector> GetApplicable(InspectorContext context) =>
        _inspectors
            .Where(inspector => inspector.CanInspect(context))
            .OrderBy(inspector => inspector.SortOrder)
            .ThenBy(inspector => inspector.DisplayName, StringComparer.Ordinal)
            .ToArray();
}
