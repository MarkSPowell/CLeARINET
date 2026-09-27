namespace Clearinet.Extensibility.Inspection.Inspectors;

/// <summary>
/// Fiddler Classic's "Headers" inspector. Always applicable -- even a
/// body-less request or response still has headers worth showing.
/// </summary>
public sealed class HeadersInspector : IInspector
{
    public string Id => "clearinet.headers";

    public string DisplayName => "Headers";

    public int SortOrder => 0;

    public bool CanInspect(InspectorContext context) => true;

    public InspectorContent Inspect(InspectorContext context)
    {
        var rows = new List<HeaderRow>(context.Headers.Count);
        foreach (var (name, value) in context.Headers)
        {
            rows.Add(new HeaderRow(name, value));
        }

        return new KeyValueContent(rows);
    }
}
