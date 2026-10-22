using Violens.ProxyCore.Certificates;

namespace Violens.AppCore.Migration;

/// <summary>
/// Removes the root certificates VioLens made under its former name,
/// CLeARINET, so a renamed install doesn't leave a trusted root behind that
/// nothing uses (or can remove).
///
/// Call it only once the new root is trusted (so a declined prompt for the
/// new one never leaves the user with neither), and only when
/// <see cref="FormerNameMigration.Result.FoundFormerInstall"/> says this
/// computer had CLeARINET: another project with a similar name could, in
/// principle, use the same certificate name.
/// </summary>
public static class FormerRoots
{
    /// <summary>The common-name prefix CLeARINET's roots used.</summary>
    public const string SubjectPrefix = "DO_NOT_TRUST_ClearinetRoot";

    /// <summary>
    /// Removes any CLeARINET root from the trusted-root store and the
    /// personal store (see <see cref="CertificateAuthority.RemoveRoots"/>).
    /// On Windows each removal from the trusted-root store shows the
    /// system's own confirmation; a "No" is respected, logged, and asked
    /// again next time. Never throws.
    /// </summary>
    /// <returns>How many old roots were removed from the trust store.</returns>
    public static int Remove(Action<string>? log = null) =>
        CertificateAuthority.RemoveRoots(SubjectPrefix, log);
}
