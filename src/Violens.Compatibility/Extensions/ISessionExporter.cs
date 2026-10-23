using Violens.ProxyCore.Sessions;

namespace Violens.Compatibility.Extensions;

/// <summary>
/// Fiddler's <c>ISessionExporter</c>
/// (fiddlerbook.com/fiddler/dev/ISessionExport.asp): adds an export format.
/// </summary>
public interface ISessionExporter : IDisposable
{
    /// <summary>
    /// Exports the sessions in <paramref name="exportFormat"/>, one of this
    /// exporter's <see cref="ProfferFormatAttribute"/> names. Returns whether it
    /// succeeded. <paramref name="progress"/> may be null.
    /// </summary>
    bool ExportSessions(
        string exportFormat,
        IReadOnlyList<Session> sessions,
        IReadOnlyDictionary<string, object> options,
        Action<ProgressCallbackEventArgs>? progress);
}
