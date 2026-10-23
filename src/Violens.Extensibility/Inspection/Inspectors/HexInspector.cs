namespace Violens.Extensibility.Inspection.Inspectors;

/// <summary>Fiddler Classic's HexView: the raw bytes, for any body. The host lays out the dump.</summary>
public sealed class HexInspector : IInspector
{
    public string Id => "violens.hex";

    public string DisplayName => "Hex";

    public int SortOrder => 20;

    public bool CanInspect(InspectorContext context) => true;

    public InspectorContent Inspect(InspectorContext context) => new HexContent(context.Body);
}
