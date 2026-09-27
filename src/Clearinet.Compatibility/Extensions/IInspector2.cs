using Clearinet.Compatibility.FiddlerScript;

namespace Clearinet.Compatibility.Extensions;

/// <summary>
/// CLeARINET's equivalent of Fiddler Classic's <c>Fiddler.Inspector2</c> base
/// type, and its two marker descendants <c>IRequestInspector2</c>/
/// <c>IResponseInspector2</c> (fiddlerbook.com/fiddler/dev/Inspectors.asp) --
/// the surface a .NET extension author implements to add a compiled
/// inspector, the binary-extension counterpart to a plain FiddlerScript-side
/// <c>Clearinet.Extensibility.Inspection.IInspector</c>.
///
/// Deliberately narrower than real Fiddler's own type: this omits
/// <c>void AddToTab(TabPage o)</c> and <c>int GetOrder()</c> entirely.
/// <c>AddToTab</c> hands the inspector a live WinForms <c>TabPage</c> to
/// populate itself -- fundamentally incompatible with the "data in,
/// rendering out" design tenet <c>Clearinet.Extensibility.Inspection.IInspector</c>'s
/// own remarks already establish (CLeARINET's UI layer owns all rendering;
/// an inspector never touches a control directly, Avalonia or otherwise).
/// This is a flagged, deliberate cut, not a silent gap -- see the .NET
/// Extension Compatibility Design doc's own section on this interface for
/// the full reasoning. <c>GetOrder()</c> is likewise dropped in favor of the
/// existing <c>IInspector.SortOrder</c>-style convention <see cref="Inspector2Adapter"/>
/// derives instead.
///
/// What IS kept is exactly what lets a body-transforming inspector still
/// work end-to-end without <c>AddToTab</c>: the host sets <see cref="headers"/>
/// and <see cref="body"/> before asking the inspector to render, and reads
/// <see cref="body"/> back afterward -- so an inspector that rewrites the
/// body it's given (the common real-world case: pretty-printing, decoding,
/// re-encoding) still round-trips correctly even though nothing here ever
/// touches a tab control.
/// </summary>
public interface IInspector2
{
    /// <summary>The request or response headers being inspected. Settable, matching real Fiddler's own field -- some inspectors edit headers directly rather than through the body.</summary>
    ExchangeHeaders? headers { get; set; }

    /// <summary>The request or response body being inspected. Settable and re-readable -- see this interface's own remarks on why that's the one piece of real Fiddler's <c>AddToTab</c>-based flow this design preserves.</summary>
    byte[]? body { get; set; }

    /// <summary>Whether this inspector has pending edits the host should write back. Matches real Fiddler's own <c>bDirty</c> flag name.</summary>
    bool bDirty { get; }

    /// <summary>Whether this inspector is showing a read-only (already-sent) exchange, matching real Fiddler's own <c>bReadOnly</c> flag name -- CLeARINET's inspectors are read-only-in-practice today (there's no live request/response editing UI yet), but the flag is kept for source compatibility with extensions that check it.</summary>
    bool bReadOnly { get; set; }

    /// <summary>Resets this inspector to its empty state, matching real Fiddler's own <c>Clear()</c> -- called between exchanges so a stateful inspector doesn't leak the previous selection's data into the next.</summary>
    void Clear();
}

/// <summary>Marker interface for a compiled inspector that wants to inspect requests -- matches real Fiddler's own <c>IRequestInspector2</c> name exactly, so an extension author's own <c>class MyInspector : IRequestInspector2</c> ports unchanged.</summary>
public interface IRequestInspector2 : IInspector2
{
}

/// <summary>Marker interface for a compiled inspector that wants to inspect responses -- matches real Fiddler's own <c>IResponseInspector2</c> name exactly.</summary>
public interface IResponseInspector2 : IInspector2
{
}
