using Clearinet.ProxyCore.Http;

namespace Clearinet.Compatibility.Extensions;

/// <summary>
/// One session as handed back from an <c>ISessionImporter</c> -- shaped
/// exactly to match what <c>Clearinet.ProxyCore.Sessions.SessionStore.Add</c>
/// needs (see that method's own call shape, already used by
/// <c>Clearinet.ProxyCore.Sessions.SazReader</c> for CLeARINET's own native
/// SAZ import), rather than real Fiddler's own <c>Session</c> type.
///
/// Real Fiddler's <c>ISessionImporter.ImportSessions</c> returns
/// <c>Session[]</c> directly, freely constructed by the importer itself.
/// <c>Clearinet.ProxyCore.Sessions.Session</c> is technically public-
/// constructible too (it's an ordinary positional record, not sealed off
/// the way this comment used to claim) -- but constructing one directly
/// would mean the importer picks its own <c>Id</c>, and only
/// <c>SessionStore</c> is allowed to hand those out: <c>SessionStore.Add</c>
/// assigns the real, authoritative, sequential <c>Id</c> itself and doesn't
/// take a pre-built <c>Session</c> as a parameter at all (see its own
/// signature). A <c>Session</c> built by an importer with a guessed
/// <c>Id</c> would either collide with a real one or just be silently
/// wrong the moment <c>SessionStore.Add</c> reassigns it anyway. This
/// record is the CLeARINET-native stand-in that sidesteps the whole
/// question: an extension author's importer builds a list of these
/// instead, carrying no <c>Id</c> at all, and the host (at the File &gt;
/// Import call site) feeds each one through <c>SessionStore.Add</c>
/// itself, exactly as <c>SazReader</c> already does for its own import
/// path.
///
/// <paramref name="Flags"/> carries Fiddler-style per-session string
/// flags through to <c>SessionStore.Add</c> -- optional, so an importer
/// that has none (every native one so far) doesn't need to know it exists.
/// The Fiddler-shaped importer adapter (<see cref="CompatShimImporterAdapter"/>)
/// fills it from <c>Clearinet.CompatShim.Session.oFlags</c>.
/// </summary>
public sealed record ImportedSession(
    string Host,
    DateTimeOffset StartedAt,
    CapturedRequest Request,
    CapturedResponse Response,
    IReadOnlyDictionary<string, string>? Flags = null)
{
    /// <summary>
    /// The original four-argument constructor, kept for binary
    /// compatibility. Adding the optional <see cref="Flags"/> parameter
    /// changed the primary constructor's compiled signature, so an
    /// extension built before that (e.g. an older build of
    /// Clearinet.SampleExtension) calls this exact overload and would
    /// otherwise fail at run time with MissingMethodException. Source that
    /// passes four arguments still binds to this one, which is fine.
    /// Guarded by BinaryCompatibilityTests.
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
/// CLeARINET's equivalent of Fiddler Classic's own <c>ISessionImporter</c>
/// (fiddlerbook.com/fiddler/dev/ISessionExport.asp) -- the surface a .NET
/// extension implements to add a new "Import Sessions..." source (e.g. a
/// different capture-file format, or a live feed from another tool) beyond
/// CLeARINET's own built-in SAZ import.
///
/// <c>options</c> is a simplified stand-in for real Fiddler's own free-form
/// options dictionary (its exact real-world shape isn't pinned down by the
/// public docs available while designing this) -- <c>IReadOnlyDictionary&lt;string,
/// object&gt;</c> covers the documented use case (format-specific settings
/// passed through from a dialog the host shows) without guessing at
/// anything more specific.
/// </summary>
public interface ISessionImporter : IDisposable
{
    /// <summary>
    /// Imports sessions from the named format. <paramref name="importFormat"/>
    /// matches one of this importer's own <see cref="ProfferFormatAttribute.FormatName"/>
    /// values (an importer proffering more than one format checks which was
    /// requested). <paramref name="progress"/> is invoked periodically so the
    /// host can show progress and let the user cancel via
    /// <see cref="ProgressCallbackEventArgs.Cancel"/> -- may be <see langword="null"/>
    /// if the host isn't showing progress UI for this call.
    /// </summary>
    IReadOnlyList<ImportedSession> ImportSessions(
        string importFormat,
        IReadOnlyDictionary<string, object> options,
        Action<ProgressCallbackEventArgs>? progress);
}
