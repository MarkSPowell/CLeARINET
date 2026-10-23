using System.Text;
using Violens.Compatibility.FiddlerScript;
using Violens.Extensibility.Inspection;

namespace Violens.Compatibility.Extensions;

/// <summary>
/// Presents an <see cref="IInspector2"/> as an <see cref="IInspector"/>, so
/// extension inspectors appear like built-in ones. Sets headers then body,
/// reads the body back, and shows it as UTF-8 text; with no
/// <c>AddToTab</c>, an extension's custom controls can't be shown.
/// </summary>
public sealed class Inspector2Adapter : IInspector
{
    /// <summary>The sort order for extension inspector tabs, after the built-in ones; ties go by name.</summary>
    public const int DefaultSortOrder = 1000;

    private readonly IInspector2 _inner;

    private Inspector2Adapter(IInspector2 inner, InspectorSide side, string id, string displayName, int sortOrder)
    {
        _inner = inner;
        Side = side;
        Id = id;
        DisplayName = displayName;
        SortOrder = sortOrder;
    }

    /// <summary>Wraps a request inspector. Without a name, the type's name is used.</summary>
    public static Inspector2Adapter ForRequest(IRequestInspector2 inner, string? displayName = null, int sortOrder = DefaultSortOrder) =>
        new(inner, InspectorSide.Request, $"extension.{inner.GetType().FullName}", displayName ?? inner.GetType().Name, sortOrder);

    /// <summary>Wraps a response inspector.</summary>
    public static Inspector2Adapter ForResponse(IResponseInspector2 inner, string? displayName = null, int sortOrder = DefaultSortOrder) =>
        new(inner, InspectorSide.Response, $"extension.{inner.GetType().FullName}", displayName ?? inner.GetType().Name, sortOrder);

    /// <summary>Which side this adapter was built for -- fixed at construction, matching which of <see cref="IRequestInspector2"/>/<see cref="IResponseInspector2"/> the wrapped instance implements.</summary>
    public InspectorSide Side { get; }

    /// <inheritdoc/>
    public string Id { get; }

    /// <inheritdoc/>
    public string DisplayName { get; }

    /// <inheritdoc/>
    public int SortOrder { get; }

    /// <inheritdoc/>
    public bool CanInspect(InspectorContext context) => context.Side == Side;

    /// <summary>No try/catch: the caller catches per inspector, as for built-in ones.</summary>
    public InspectorContent Inspect(InspectorContext context)
    {
        _inner.Clear();

        // Inspectors only ever see captured sessions.
        _inner.bReadOnly = true;

        _inner.headers = new ExchangeHeaders(context.Headers);
        _inner.body = context.Body;

        var resultBytes = _inner.body ?? context.Body;
        return new TextContent(Encoding.UTF8.GetString(resultBytes));
    }
}
