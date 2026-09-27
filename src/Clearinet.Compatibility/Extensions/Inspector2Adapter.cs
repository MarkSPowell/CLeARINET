using System.Text;
using Clearinet.Compatibility.FiddlerScript;
using Clearinet.Extensibility.Inspection;

namespace Clearinet.Compatibility.Extensions;

/// <summary>
/// Adapts a loaded <see cref="IInspector2"/> (a .NET extension's compiled
/// inspector) into <see cref="Clearinet.Extensibility.Inspection.IInspector"/>
/// -- the same interface CLeARINET's own first-party inspectors
/// (<c>HeadersInspector</c>, <c>RawTextInspector</c>, <c>HexInspector</c>)
/// implement, so <c>InspectorRegistry</c> and <c>MainWindowViewModel.RefreshInspectors</c>
/// treat an extension-provided inspector exactly like a built-in one: no
/// special-casing anywhere in the UI layer.
///
/// The mechanics this adapter is built around -- see <see cref="IInspector2"/>'s
/// own remarks for why the underlying interface looks the way it does:
/// <see cref="Inspect"/> feeds the context's headers and body into
/// <see cref="IInspector2.headers"/>/<see cref="IInspector2.body"/> (headers
/// first, since a real inspector's body handling commonly depends on having
/// already seen Content-Type), then reads <see cref="IInspector2.body"/>
/// back out -- whatever the extension did in response to those two setters
/// (decode, pretty-print, transform) is exactly what this adapter surfaces.
/// There is no method on <see cref="IInspector2"/> that hands back arbitrary
/// UI content (that was <c>AddToTab</c>'s job in real Fiddler, and it's
/// gone -- see that interface's own remarks), so the only thing this
/// adapter can produce is <see cref="TextContent"/> from the resulting
/// bytes, decoded as UTF-8. That's a real, flagged limitation: an extension
/// that populated a fully custom control (an image preview, a tree view) in
/// real Fiddler's <c>AddToTab</c> has no equivalent surface here today --
/// consistent with <see cref="InspectorContent"/>'s own remarks that an
/// image/tree case is future work, not yet built for ANY inspector, first-
/// or third-party.
/// </summary>
public sealed class Inspector2Adapter : IInspector
{
    /// <summary>Where a compiled inspector's tab sorts relative to CLeARINET's own built-ins (Headers/Raw/Hex all sort well below this). Real Fiddler's <c>GetOrder()</c> isn't available to ask (see <see cref="IInspector2"/>'s own remarks on why) -- every adapted inspector gets this same fixed value, with ties broken by <see cref="DisplayName"/> per <see cref="IInspector.SortOrder"/>'s own documented tie-break rule.</summary>
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

    /// <summary>Wraps a loaded <see cref="IRequestInspector2"/>. <paramref name="displayName"/>/<paramref name="sortOrder"/> default to the wrapped type's own name and <see cref="DefaultSortOrder"/> -- an extension has no member to declare either explicitly (see this class's own remarks), so a host that wants something friendlier than a raw type name can override it when constructing this adapter.</summary>
    public static Inspector2Adapter ForRequest(IRequestInspector2 inner, string? displayName = null, int sortOrder = DefaultSortOrder) =>
        new(inner, InspectorSide.Request, $"extension.{inner.GetType().FullName}", displayName ?? inner.GetType().Name, sortOrder);

    /// <summary>Wraps a loaded <see cref="IResponseInspector2"/> -- see <see cref="ForRequest"/>'s own remarks.</summary>
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

    /// <summary>
    /// Deliberately has no try/catch of its own -- an extension that throws
    /// here propagates straight out to <c>MainWindowViewModel.AddInspectorTabs</c>'s
    /// own per-inspector try/catch (see that call site), the exact same
    /// safety net every other <see cref="IInspector"/> already relies on.
    /// Adding a second layer here would just hide which layer actually
    /// caught it.
    /// </summary>
    public InspectorContent Inspect(InspectorContext context)
    {
        _inner.Clear();

        // CLeARINET has no live request/response editing UI yet (see
        // IInspector2's own remarks) -- every adapted inspector is always
        // shown a read-only, already-captured exchange.
        _inner.bReadOnly = true;

        _inner.headers = new ExchangeHeaders(context.Headers);
        _inner.body = context.Body;

        var resultBytes = _inner.body ?? context.Body;
        return new TextContent(Encoding.UTF8.GetString(resultBytes));
    }
}
