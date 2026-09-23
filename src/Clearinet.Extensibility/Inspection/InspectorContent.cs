namespace Clearinet.Extensibility.Inspection;

/// <summary>
/// The small, closed set of shapes an inspector can hand back. Closed on
/// purpose: a host needs exactly one renderer per case here, ever, even
/// for a third-party inspector it has never seen before. Growing this set
/// is a breaking change to the whole plugin API, so it should only happen
/// when an existing case genuinely can't express something new -- an
/// image preview and a structured (JSON/XML) tree view are the two most
/// likely next additions, once something actually needs them.
/// </summary>
public abstract record InspectorContent;

/// <summary>
/// Plain or syntax-hinted text -- the common case (Raw, and eventually a
/// pretty-printed JSON/XML/WebForms view). <see cref="SyntaxHint"/> is a
/// free-form hint ("json", "xml", "html") a host MAY use for highlighting;
/// nothing requires it, and a host that ignores it still renders correct,
/// readable text.
/// </summary>
public sealed record TextContent(string Text, string? SyntaxHint = null) : InspectorContent;

/// <summary>One name/value row in a <see cref="KeyValueContent"/> table.</summary>
public sealed record HeaderRow(string Name, string Value);

/// <summary>
/// An ordered list of name/value pairs -- Headers, and eventually Cookies
/// or parsed query-string parameters. A dedicated record with real
/// properties rather than a raw tuple list on purpose: this crosses into
/// UI binding, and a plain <c>(string, string)</c> tuple's element names
/// are a compile-time-only alias -- reflection (which classic, non-compiled
/// bindings use) sees <c>Item1</c>/<c>Item2</c>, not <c>Name</c>/<c>Value</c>,
/// so a tuple here would silently render blank rows instead of failing loudly.
/// </summary>
public sealed record KeyValueContent(IReadOnlyList<HeaderRow> Rows) : InspectorContent;

/// <summary>Raw bytes, for a hex/ASCII dump. Formatting them into lines is a rendering concern, not this record's job.</summary>
public sealed record HexContent(byte[] Bytes) : InspectorContent;

/// <summary>
/// An inspector applied but couldn't make sense of this particular body
/// (a Content-Encoding it doesn't handle, malformed JSON claiming to be
/// JSON). Shown as a message instead of the tab going silently blank, or
/// -- for a third-party inspector -- an unhandled exception taking the
/// whole detail pane down.
/// </summary>
public sealed record ErrorContent(string Message) : InspectorContent;
