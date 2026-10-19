using Violens.ProxyCore.Sessions;

namespace Violens.Compatibility.Extensions;

/// <summary>
/// VioLens's equivalent of Fiddler Classic's own <c>ISessionExporter</c>
/// (fiddlerbook.com/fiddler/dev/ISessionExport.asp) -- the surface a .NET
/// extension implements to add a new "Export Sessions..." destination
/// beyond VioLens's own built-in SAZ export.
///
/// Unlike <see cref="ISessionImporter"/>, this uses the real
/// <c>Violens.ProxyCore.Sessions.Session</c> type directly rather than a
/// stand-in record: export only ever reads already-captured sessions (never
/// constructs new ones), so there's no public-constructor problem here --
/// this matches <c>Violens.ProxyCore.Sessions.SazWriter.Write</c>'s own
/// existing <c>IReadOnlyList&lt;Session&gt;</c> signature shape exactly.
/// </summary>
public interface ISessionExporter : IDisposable
{
    /// <summary>
    /// Exports the given sessions to the named format, returning
    /// <see langword="true"/> on success. <paramref name="exportFormat"/>
    /// matches one of this exporter's own <see cref="ProfferFormatAttribute.FormatName"/>
    /// values. <paramref name="options"/> is a simplified stand-in for real
    /// Fiddler's own free-form options dictionary -- see
    /// <see cref="ISessionImporter.ImportSessions"/>'s own remarks on the
    /// same simplification. <paramref name="progress"/> may be
    /// <see langword="null"/> if the host isn't showing progress UI for this
    /// call.
    /// </summary>
    bool ExportSessions(
        string exportFormat,
        IReadOnlyList<Session> sessions,
        IReadOnlyDictionary<string, object> options,
        Action<ProgressCallbackEventArgs>? progress);
}
