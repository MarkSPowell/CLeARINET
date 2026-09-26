using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Clearinet.Compatibility.FiddlerScript;
using Clearinet.ProxyCore.Sessions;

namespace Clearinet.DesktopUi.Models;

/// <summary>
/// A UI-friendly, already-formatted projection of a captured <see cref="Session"/>
/// for display in the session list. Deliberately not the domain type itself:
/// the list binds to plain strings/ints it can show as-is, and formatting
/// choices (time zone, byte units) live here rather than leaking into
/// <see cref="Session"/>, which other consumers (SazWriter, inspectors)
/// need untouched. <see cref="Session"/> is still carried along -- not
/// projected into strings -- because the detail pane's inspectors need the
/// real headers and body bytes once a row is selected, not their grid
/// display text.
/// </summary>
public sealed class SessionRow
{
    private static readonly IReadOnlyDictionary<string, string> NoScriptColumns = new Dictionary<string, string>();

    public required int Id { get; init; }
    public required string Time { get; init; }
    public required int StatusCode { get; init; }
    public required string Method { get; init; }
    public required string Url { get; init; }
    public required string RequestSize { get; init; }
    public required string ResponseSize { get; init; }
    public required Session Session { get; init; }

    /// <summary>
    /// One entry per <c>[BindUIColumn]</c> method the loaded script (if any)
    /// declared at the moment this row was built, keyed by
    /// <see cref="UIColumnDescriptor.ColumnTitle"/> -- what
    /// <c>MainWindow.axaml.cs</c>'s dynamically-added
    /// <c>DataGridTextColumn</c>s bind to via an indexer
    /// (<c>{Binding ScriptColumns[Title]}</c>). Computed once, here, at row
    /// creation -- a session added before a script (re)loaded a new column,
    /// or before one was removed, keeps whatever this dictionary held at
    /// capture time rather than being recomputed retroactively; see
    /// <see cref="From"/>'s own remarks on why that's an accepted,
    /// documented simplification rather than a hidden gap.
    /// </summary>
    public required IReadOnlyDictionary<string, string> ScriptColumns { get; init; }

    /// <summary>
    /// The session's flags by name (case-insensitive), for session-list
    /// columns an extension binds to a flag (<c>{Binding Flags[X-Privacy]}</c>);
    /// a missing flag reads as an empty string.
    /// </summary>
    public FlagLookup Flags { get; private init; } = FlagLookup.Empty;

    /// <summary>How the session list draws this row, from its <c>ui-*</c> flags. See <see cref="SessionRowStyle"/>.</summary>
    public SessionRowStyle Style { get; private init; } = SessionRowStyle.Plain;

    /// <param name="scriptRunner">
    /// The currently-loaded FiddlerScript's own runner, if any -- when it
    /// declares one or more <c>[BindUIColumn]</c> methods,
    /// <see cref="ScriptColumns"/> is computed against THIS session right
    /// now, once. <see langword="null"/> (or a runner with no loaded script,
    /// or no <c>BindUIColumn</c> methods) leaves <see cref="ScriptColumns"/>
    /// empty -- there's deliberately no "recompute every row when the
    /// script reloads" pass; a script that adds/changes a custom column
    /// only affects sessions captured from that point on, the same way
    /// changing <c>OnBeforeResponse</c> logic doesn't retroactively re-edit
    /// already-captured responses either.
    /// </param>
    public static SessionRow From(Session session, FiddlerScriptRunner? scriptRunner = null) => new()
    {
        Id = session.Id,
        // Local time: this is a desktop app showing what just happened on
        // this machine, not a log file where UTC would matter more.
        Time = session.StartedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture),
        StatusCode = session.Response.StatusCode,
        Method = session.Request.Method,
        Url = $"https://{session.Host}{session.Request.Target}",
        RequestSize = FormatBytes(session.Request.Body.Length),
        ResponseSize = FormatBytes(session.Response.Body.Length),
        Session = session,
        ScriptColumns = BuildScriptColumns(session, scriptRunner),
        Flags = new FlagLookup(session.Flags),
        Style = SessionRowStyle.From(session.Flags),
    };

    private static string FormatBytes(int bytes) => bytes < 1024
        ? bytes.ToString(CultureInfo.InvariantCulture) + " B"
        : (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB";

    private static IReadOnlyDictionary<string, string> BuildScriptColumns(Session session, FiddlerScriptRunner? scriptRunner)
    {
        if (scriptRunner is null || scriptRunner.Directives.UIColumns.Count == 0)
        {
            return NoScriptColumns;
        }

        var columns = new Dictionary<string, string>();
        foreach (var column in scriptRunner.Directives.UIColumns)
        {
            columns[column.ColumnTitle] = scriptRunner.ComputeUIColumnValue(column.MethodName, session);
        }

        return columns;
    }
}

/// <summary>A session's flags, readable by name from a binding path; a missing one is an empty string.</summary>
public sealed class FlagLookup
{
    public static readonly FlagLookup Empty = new(null);

    private readonly IReadOnlyDictionary<string, string>? _flags;

    public FlagLookup(IReadOnlyDictionary<string, string>? flags) => _flags = flags;

    public string this[string name]
    {
        get
        {
            if (_flags is null)
            {
                return string.Empty;
            }

            if (_flags.TryGetValue(name, out var value))
            {
                return value;
            }

            // Flags from older sources may not use a case-insensitive
            // dictionary; Fiddler flag names never depend on case.
            foreach (var (key, candidate) in _flags)
            {
                if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }
    }
}

/// <summary>
/// How a row looks, from the Fiddler Classic flags an importer, extension or
/// script set on its session:
/// <list type="bullet">
/// <item><c>ui-backcolor</c> / <c>ui-color</c>: background / text colour,
/// as a name (<c>Lime</c>) or <c>#RRGGBB</c>. A background without a text
/// colour gets black or white text, whichever reads better on it, so the
/// row stays readable in both light and dark themes.</item>
/// <item><c>ui-bold</c>, <c>ui-italic</c>, <c>ui-strikeout</c>: present
/// (any value) to turn on.</item>
/// <item><c>ui-hide</c>: present to leave the row out of the list.</item>
/// </list>
/// An unrecognised colour is ignored.
/// </summary>
public sealed record SessionRowStyle(
    IBrush? Background,
    IBrush? Foreground,
    bool Bold,
    bool Italic,
    bool Strikeout,
    bool Hidden)
{
    public static readonly SessionRowStyle Plain = new(null, null, false, false, false, false);

    public bool IsPlain => this == Plain;

    public static SessionRowStyle From(IReadOnlyDictionary<string, string>? flags)
    {
        if (flags is null || flags.Count == 0)
        {
            return Plain;
        }

        var lookup = new FlagLookup(flags);
        Color? back = ParseColor(lookup["ui-backcolor"]);
        Color? fore = ParseColor(lookup["ui-color"]);
        if (back is { } background && fore is null)
        {
            fore = ReadableTextOn(background);
        }

        var style = new SessionRowStyle(
            back is { } b ? new ImmutableSolidColorBrush(b) : null,
            fore is { } f ? new ImmutableSolidColorBrush(f) : null,
            Has(flags, "ui-bold"),
            Has(flags, "ui-italic"),
            Has(flags, "ui-strikeout"),
            Has(flags, "ui-hide"));

        return style == Plain ? Plain : style;
    }

    private static bool Has(IReadOnlyDictionary<string, string> flags, string name) =>
        flags.Keys.Any(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));

    private static Color? ParseColor(string text) =>
        !string.IsNullOrWhiteSpace(text) && Color.TryParse(text.Trim(), out var color) ? color : null;

    /// <summary>Black or white, whichever contrasts more with <paramref name="background"/> (WCAG relative luminance).</summary>
    private static Color ReadableTextOn(Color background)
    {
        static double Linear(byte channel)
        {
            var c = channel / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        var luminance = (0.2126 * Linear(background.R)) + (0.7152 * Linear(background.G)) + (0.0722 * Linear(background.B));
        return luminance > 0.179 ? Colors.Black : Colors.White;
    }
}
