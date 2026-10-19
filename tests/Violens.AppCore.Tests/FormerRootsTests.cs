using Violens.AppCore.Migration;
using Violens.ProxyCore.Certificates;
using Xunit;

namespace Violens.AppCore.Tests;

public class FormerRootsTests
{
    /// <summary>
    /// FormerRoots finds old roots by subject-name substring, so VioLens's
    /// root name must never contain CLeARINET's (or the other way round) --
    /// otherwise it would remove the root VioLens just installed.
    /// </summary>
    [Fact]
    public void TheFormerRootNameNeverMatchesTheCurrentRoot()
    {
        using var root = CertificateAuthority.GenerateRoot(
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddYears(1), CertificateAuthority.SubjectPrefix);

        Assert.DoesNotContain(FormerRoots.SubjectPrefix, root.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(CertificateAuthority.SubjectPrefix, FormerRoots.SubjectPrefix, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("DO_NOT_TRUST_", FormerRoots.SubjectPrefix, StringComparison.Ordinal);
    }
}
