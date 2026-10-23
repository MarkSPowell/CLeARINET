namespace Violens.ProxyCore;

/// <summary>
/// VioLens's version, for gating extensions the way Fiddler's
/// <c>RequiredVersion</c> attribute does. Bumped by hand.
/// </summary>
public static class ViolensVersion
{
    public static readonly Version Current = new(0, 1, 0, 0);
}
