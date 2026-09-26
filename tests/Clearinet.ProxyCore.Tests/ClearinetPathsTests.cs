using Xunit;

namespace Clearinet.ProxyCore.Tests;

/// <summary>
/// Pins where settings live on each platform -- the Preferences Design
/// doc's "Location" row of its parity checklist. Runs on whichever OS the
/// CI leg is; the assertion for the other OS simply doesn't apply there.
/// If a future .NET changes what <c>LocalApplicationData</c> maps to, this
/// fails instead of silently moving everyone's settings.
/// </summary>
public class ClearinetPathsTests
{
    [Fact]
    public void AppDataFolderIsUnderThePlatformsPerUserAppDataRoot()
    {
        var folder = ClearinetPaths.AppDataFolder;

        Assert.Equal("CLeARINET", Path.GetFileName(folder));
        Assert.True(Path.IsPathFullyQualified(folder));

        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            Assert.False(string.IsNullOrEmpty(localAppData));
            Assert.Equal(Path.Combine(localAppData!, "CLeARINET"), folder, ignoreCase: true);
        }
        else if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            Assert.False(string.IsNullOrEmpty(home));
            Assert.Equal(Path.Combine(home!, "Library", "Application Support", "CLeARINET"), folder);
        }
    }
}
