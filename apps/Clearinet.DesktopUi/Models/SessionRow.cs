using System.Globalization;
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
    public required int Id { get; init; }
    public required string Time { get; init; }
    public required int StatusCode { get; init; }
    public required string Method { get; init; }
    public required string Url { get; init; }
    public required string RequestSize { get; init; }
    public required string ResponseSize { get; init; }
    public required Session Session { get; init; }

    public static SessionRow From(Session session) => new()
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
    };

    private static string FormatBytes(int bytes) => bytes < 1024
        ? bytes.ToString(CultureInfo.InvariantCulture) + " B"
        : (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
}
