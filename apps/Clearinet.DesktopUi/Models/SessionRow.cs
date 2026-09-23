using System.Globalization;
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
