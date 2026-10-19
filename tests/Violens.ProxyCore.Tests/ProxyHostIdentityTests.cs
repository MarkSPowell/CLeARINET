using Xunit;

namespace Violens.ProxyCore.Tests;

/// <summary>
/// The names ProxyCore puts on what it creates for its host app. Only
/// constructs identities; none of these tests calls ProxyEngine.Configure,
/// which is process-wide and would race with tests running in parallel.
/// </summary>
public class ProxyHostIdentityTests
{
    [Fact]
    public void TheDefaultKeepsVioLensesExistingNames()
    {
        var identity = ProxyHostIdentity.Default;

        Assert.Equal("VioLens", identity.ProductName);
        Assert.Equal("DO_NOT_TRUST_ViolensRoot", identity.RootCertificateName);
        Assert.Equal(Certificates.CertificateAuthority.SubjectPrefix, identity.RootCertificateName);
        Assert.Equal("violens", identity.ProxyHostName);
        Assert.Equal("ViolensRoot.cer", identity.RootCertificateFileName);
    }

    /// <summary>
    /// Pins where settings live on each platform -- the Preferences Design
    /// doc's "Location" row. If a future .NET changes what
    /// LocalApplicationData maps to, this fails instead of silently moving
    /// everyone's settings.
    /// </summary>
    [Fact]
    public void TheDefaultDataFolderIsUnderThePlatformsPerUserAppDataRoot()
    {
        var folder = ProxyHostIdentity.Default.ResolveDataFolder();

        Assert.Equal("VioLens", Path.GetFileName(folder));
        Assert.True(Path.IsPathFullyQualified(folder));

        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            Assert.False(string.IsNullOrEmpty(localAppData));
            Assert.Equal(Path.Combine(localAppData!, "VioLens"), folder, ignoreCase: true);
        }
        else if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            Assert.False(string.IsNullOrEmpty(home));
            Assert.Equal(Path.Combine(home!, "Library", "Application Support", "VioLens"), folder);
        }
    }

    [Fact]
    public void ANewIdentityDerivesItsNamesFromTheProductName()
    {
        var identity = new ProxyHostIdentity("My App 2");

        Assert.Equal("My App 2", identity.ProductName);
        Assert.Equal("DO_NOT_TRUST_MyApp2Root", identity.RootCertificateName);
        Assert.Equal("myapp2", identity.ProxyHostName);
        Assert.Equal("MyApp2Root.cer", identity.RootCertificateFileName);
        Assert.Equal("My App 2", Path.GetFileName(identity.ResolveDataFolder()));
        Assert.Equal(string.Empty, identity.ProductVersion);
    }

    [Fact]
    public void AnyNameCanBeSetExplicitly()
    {
        var folder = Path.Combine(Path.GetTempPath(), "somewhere");
        var identity = new ProxyHostIdentity("Contoso Web Debugger")
        {
            ProductVersion = "1.2.3",
            RootCertificateName = "DO_NOT_TRUST_ContosoRoot",
            ProxyHostName = "contoso.proxy",
            DataFolder = folder,
        };

        Assert.Equal("DO_NOT_TRUST_ContosoRoot", identity.RootCertificateName);
        Assert.Equal("contoso.proxy", identity.ProxyHostName);
        Assert.Equal(folder, identity.ResolveDataFolder());
        Assert.Equal("ContosoWebDebuggerRoot.cer", identity.RootCertificateFileName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("---")]
    [InlineData("a/b")]
    [InlineData("..")]
    public void UnusableProductNamesAreRefused(string name) =>
        Assert.Throws<ArgumentException>(() => new ProxyHostIdentity(name));

    [Fact]
    public void AnotherAppsRootCertificateIsNamedForThatApp()
    {
        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-1);
        using var root = Certificates.CertificateAuthority.GenerateRoot(
            notBefore, notBefore.AddDays(1), new ProxyHostIdentity("Contoso").RootCertificateName);

        Assert.Contains("DO_NOT_TRUST_ContosoRoot (", root.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain(Certificates.CertificateAuthority.SubjectPrefix, root.Subject, StringComparison.OrdinalIgnoreCase);
    }
}
