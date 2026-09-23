using Clearinet.ProxyCore.Http;

namespace Clearinet.ProxyCore.Sessions;

/// <summary>
/// Holds every session captured so far, in memory, for the lifetime of the
/// process. Good enough for this capture spike and for the SAZ writer that
/// reads from it. <see cref="SessionAdded"/> is the change feed a live UI
/// subscribes to -- Phase 2's desktop app uses it to append rows as they're
/// captured instead of polling <see cref="Snapshot"/>. Eviction and
/// on-disk spillover for long-running captures are still unsolved.
/// </summary>
public sealed class SessionStore
{
    private readonly object _gate = new();
    private readonly List<Session> _sessions = [];
    private int _nextId = 1;

    /// <summary>
    /// Raised synchronously, on whatever thread called <see cref="Add"/>,
    /// right after a session is recorded -- that's proxy connection-handling
    /// threads, never a UI thread. Subscribers that touch UI state (like
    /// the desktop app) must marshal back to the UI thread themselves
    /// rather than assuming this event already did it.
    /// </summary>
    public event Action<Session>? SessionAdded;

    public Session Add(string host, DateTimeOffset startedAt, CapturedRequest request, CapturedResponse response)
    {
        Session session;
        lock (_gate)
        {
            session = new Session(_nextId++, host, startedAt, request, response);
            _sessions.Add(session);
        }

        // Fired outside the lock: a subscriber that calls back into this
        // store (e.g. Snapshot()) during the event must not deadlock, and
        // a slow subscriber must not hold up other capture threads calling
        // Add() concurrently.
        SessionAdded?.Invoke(session);
        return session;
    }

    public IReadOnlyList<Session> Snapshot()
    {
        lock (_gate)
        {
            return _sessions.ToArray();
        }
    }

    /// <summary>
    /// A best-effort look at the id the <em>next</em> session recorded via
    /// <see cref="Add"/> will receive. Exists for FiddlerScript/extension
    /// code that wants to read <c>oSession.id</c> from inside
    /// <c>OnBeforeRequest</c>/<c>OnBeforeResponse</c> -- see
    /// <c>Clearinet.ProxyCore.Scripting.IFiddlerScriptRunner</c> -- before
    /// the session that exchange belongs to has actually been recorded here;
    /// <see cref="Add"/> only assigns a real id once both the request and
    /// the response are known, which for a request-side hook is still in the
    /// future.
    ///
    /// <b>Best-effort under concurrency, not a reservation:</b> another
    /// connection's own <see cref="Add"/> can run between this call
    /// returning and the caller's own eventual <see cref="Add"/> call, so
    /// the id actually assigned to that session can end up higher than
    /// whatever this returned. There is deliberately no way to reserve an id
    /// ahead of time -- that would need <see cref="Add"/> itself to change
    /// shape (pre-allocate, then fill in later), which nothing today
    /// requires closely enough to justify. This matches how real Fiddler's
    /// own <c>oSession.id</c> numbering works in spirit: assigned by capture
    /// order, not requested in advance.
    /// </summary>
    public int PeekNextId()
    {
        lock (_gate)
        {
            return _nextId;
        }
    }
}
