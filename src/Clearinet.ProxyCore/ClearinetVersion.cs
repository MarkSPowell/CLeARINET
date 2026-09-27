namespace Clearinet.ProxyCore;

/// <summary>
/// CLeARINET's own version, for the same purpose Fiddler Classic's own
/// build number serves in its <c>Fiddler.RequiredVersion</c> extension-
/// gating attribute -- see <c>Clearinet.Compatibility.Extensions.RequiredVersionAttribute</c>'s
/// own remarks for why a CLeARINET-native equivalent is needed rather than
/// referencing Fiddler's real type.
///
/// Hand-bumped, not build-stamped: nothing in the build pipeline sets this
/// automatically yet (see the Project Plan's "Still undecided" section on
/// versioning/release process -- a real one is still open). Starts at
/// 0.1.0.0 to reflect this project's actual pre-1.0, MVP-in-progress status
/// rather than presuming a 1.0 that hasn't happened.
/// </summary>
public static class ClearinetVersion
{
    public static readonly Version Current = new(0, 1, 0, 0);
}
