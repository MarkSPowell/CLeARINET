using Violens.Compatibility.FiddlerScript;

namespace Violens.Compatibility.Extensions;

/// <summary>
/// Fiddler's <c>Inspector2</c>, with <c>IRequestInspector2</c> and
/// <c>IResponseInspector2</c>, minus <c>AddToTab</c> and <c>GetOrder</c>:
/// VioLens inspectors return content rather than drawing into a WinForms
/// tab. The host sets <see cref="headers"/> and <see cref="body"/> and reads
/// <see cref="body"/> back, so body-transforming inspectors still work.
/// </summary>
public interface IInspector2
{
    /// <summary>The request or response headers being inspected. Settable, matching real Fiddler's own field -- some inspectors edit headers directly rather than through the body.</summary>
    ExchangeHeaders? headers { get; set; }

    /// <summary>The body being inspected. Read back after inspection, so an inspector can transform it.</summary>
    byte[]? body { get; set; }

    /// <summary>Whether this inspector has pending edits the host should write back. Matches real Fiddler's own <c>bDirty</c> flag name.</summary>
    bool bDirty { get; }

    /// <summary>Whether this inspector is showing a read-only (already-sent) exchange, matching real Fiddler's own <c>bReadOnly</c> flag name -- VioLens's inspectors are read-only-in-practice today (there's no live request/response editing UI yet), but the flag is kept for source compatibility with extensions that check it.</summary>
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
