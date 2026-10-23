using Violens.ProxyCore.Http;

namespace Violens.Extensibility.Inspection.Inspectors;

/// <summary>
/// Fiddler Classic's TextView, called Raw: the body as text, decompressed
/// and decoded with its charset by <see cref="ContentDecoder"/>. No
/// pretty-printing.
/// </summary>
public sealed class RawTextInspector : IInspector
{
    public string Id => "violens.raw";

    public string DisplayName => "Raw";

    public int SortOrder => 10;

    public bool CanInspect(InspectorContext context) => true;

    public InspectorContent Inspect(InspectorContext context)
    {
        if (context.Body.Length == 0)
        {
            return new TextContent(string.Empty);
        }

        if (!ContentDecoder.TryDecode(context.Body, context.FindHeader("Content-Encoding"), out var decoded, out var error))
        {
            return new ErrorContent(error!);
        }

        return new TextContent(ContentDecoder.DecodeText(decoded, context.FindHeader("Content-Type")));
    }
}
