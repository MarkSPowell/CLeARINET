using Violens.ProxyCore.Http;

namespace Violens.Compatibility.Extensions;

/// <summary>
/// A session returned by an importer. Unlike Fiddler, importers don't build
/// sessions themselves: only <c>SessionStore</c> assigns Ids, so the host
/// adds each of these through <c>SessionStore.Add</c>. <paramref name="Flags"/>
/// carries Fiddler-style string flags, if any.
/// </summary>
public sealed record ImportedSession(
    string Host,
    DateTimeOffset StartedAt,
    CapturedRequest Request,
    CapturedResponse Response,
    IReadOnlyDictionary<string, string>? Flags = null)
{
    /// <summary>
    /// The original four-argument constructor, kept so extensions built before
    /// <see cref="Flags"/> existed still load (see BinaryCompatibilityTests).
    /// </summary>
    public ImportedSession(string Host, DateTimeOffset StartedAt, CapturedRequest Request, CapturedResponse Response)
        : this(Host, StartedAt, Request, Response, null)
    {
    }

    /// <summary>The original four-value deconstruction, kept for the same reason as the four-argument constructor.</summary>
    public void Deconstruct(out string Host, out DateTimeOffset StartedAt, out CapturedRequest Request, out CapturedResponse Response)
    {
        Host = this.Host;
        StartedAt = this.StartedAt;
        Request = this.Request;
        Response = this.Response;
    }
}

/// <summary>
/// Fiddler's <c>ISessionImporter</c>
/// (fiddlerbook.com/fiddler/dev/ISessionExport.asp): adds an import format.
/// <c>options</c> stands in for Fiddler's loosely documented options
/// dictionary.
/// </summary>
public interface ISessionImporter : IDisposable
{
    /// <summary>
    /// Imports sessions in <paramref name="importFormat"/>, one of this
    /// importer's <see cref="ProfferFormatAttribute"/> names.
    /// <paramref name="progress"/> may be null; setting its
    /// <see cref="ProgressCallbackEventArgs.Cancel"/> cancels.
    /// </summary>
    IReadOnlyList<ImportedSession> ImportSessions(
        string importFormat,
        IReadOnlyDictionary<string, object> options,
        Action<ProgressCallbackEventArgs>? progress);
}
