namespace Clearinet.Extensibility.Inspection.Inspectors;

/// <summary>
/// Fiddler Classic's "HexView" -- a raw byte dump that always applies,
/// including for content <see cref="RawTextInspector"/> can't make sense
/// of (a zstd body today, a genuinely binary payload always). Hands back
/// the bytes unmodified; laying them out as hex/ASCII lines is a rendering
/// concern for the host, not this inspector's job.
/// </summary>
public sealed class HexInspector : IInspector
{
    public string Id => "clearinet.hex";

    public string DisplayName => "Hex";

    public int SortOrder => 20;

    public bool CanInspect(InspectorContext context) => true;

    public InspectorContent Inspect(InspectorContext context) => new HexContent(context.Body);
}
