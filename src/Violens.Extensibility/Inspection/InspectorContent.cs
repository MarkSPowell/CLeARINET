namespace Violens.Extensibility.Inspection;

/// <summary>
/// What an inspector can return. A closed set, so a host needs one renderer
/// per case even for inspectors it has never seen; adding a case is a
/// breaking change.
/// </summary>
public abstract record InspectorContent;

/// <summary>Text, with an optional hint ("json", "xml", "html") a host may use for highlighting.</summary>
public sealed record TextContent(string Text, string? SyntaxHint = null) : InspectorContent;

/// <summary>One name/value row in a <see cref="KeyValueContent"/> table.</summary>
public sealed record HeaderRow(string Name, string Value);

/// <summary>
/// Name/value rows (Headers, Cookies, WebForms). A record rather than a
/// tuple, because reflection bindings see a tuple's <c>Item1</c>/<c>Item2</c>,
/// not its names, and would show blank rows.
/// </summary>
public sealed record KeyValueContent(IReadOnlyList<HeaderRow> Rows) : InspectorContent;

/// <summary>Raw bytes, for a hex/ASCII dump. Formatting them into lines is a rendering concern, not this record's job.</summary>
public sealed record HexContent(byte[] Bytes) : InspectorContent;

/// <summary>The inspector applies but couldn't read this body; shown as a message.</summary>
public sealed record ErrorContent(string Message) : InspectorContent;

/// <summary>
/// An image to draw (the ImageView tab). <see cref="Bytes"/> is the decoded
/// image file; <see cref="MediaType"/> is its Content-Type media type, which
/// may be empty; <see cref="Summary"/> is a line of text to show with it
/// (format, pixel dimensions, size). A host that can't draw the format still
/// shows <see cref="Summary"/>.
/// </summary>
public sealed record ImageContent(byte[] Bytes, string MediaType, string Summary) : InspectorContent;
