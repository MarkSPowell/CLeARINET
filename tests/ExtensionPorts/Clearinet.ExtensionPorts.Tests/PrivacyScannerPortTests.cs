using Clearinet.Compatibility.Extensions;
using Clearinet.ProxyCore.Extensions;
using Clearinet.ProxyCore.Http;
using Clearinet.ProxyCore.Preferences;
using Xunit;
using CompatShimHost = Clearinet.CompatShim.CompatShimHost;
using FiddlerApplication = Clearinet.CompatShim.FiddlerApplication;
using ShimMenuItem = Clearinet.CompatShim.MenuItem;

namespace Clearinet.ExtensionPorts.Tests;

/// <summary>
/// Eric Lawrence's Privacy Scanner, the cookie/P3P sample in Telerik's
/// Fiddler docs, ported by ../port.ps1 (its <c>using Fiddler;</c> and
/// <c>using System.Windows.Forms;</c> lines deleted, and the
/// <c>System.Windows.Forms.</c> prefix dropped from <c>MenuItem</c>) and
/// loaded through <see cref="ExtensionHost"/>. It's the smallest real
/// extension that uses a top-level menu, a flag-bound session-list column
/// and row colours, so it tests those, headlessly: the menu and column are
/// checked on <see cref="FiddlerApplication.UI"/>, which the app draws.
///
/// These tests change <see cref="CompatShimHost"/>'s static hooks, and every
/// loaded copy of the extension adds its menu to the shared
/// <see cref="FiddlerApplication.UI"/>, so each test looks at the menu its own
/// copy added (the last one).
/// </summary>
public sealed class PrivacyScannerPortTests : IDisposable
{
    private const string PortedDllName = "PrivacyScanner.dll";
    private const string EnabledPref = "extensions.tagcookies.enabled";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "clearinet-port-tests", Guid.NewGuid().ToString("N"));
    private readonly List<string> _log = [];
    private readonly Action<string> _originalLog = CompatShimHost.Log;
    private readonly IPreferenceStore _originalPreferences = CompatShimHost.Preferences;
    private readonly IPreferenceStore _preferences = PreferenceStore.CreateInMemory();
    private readonly ExtensionHost _host;

    public PrivacyScannerPortTests()
    {
        CompatShimHost.Log = message => { lock (_log) { _log.Add(message); } };
        CompatShimHost.Preferences = _preferences;
        _preferences.SetBoolPref(EnabledPref, true);

        var extensions = Path.Combine(_folder, "Extensions");
        Directory.CreateDirectory(extensions);
        File.Copy(FindPortedDll(), Path.Combine(extensions, PortedDllName));

        _host = new ExtensionHost([extensions], log: message => { lock (_log) { _log.Add(message); } });
        _host.Load();
    }

    public void Dispose()
    {
        _host.Unload();
        CompatShimHost.Log = _originalLog;
        CompatShimHost.Preferences = _originalPreferences;

        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static ShimMenuItem PrivacyMenu => FiddlerApplication.UI.mnuMain.MenuItems.Last(m => m.Text == "Privacy");

    [Fact]
    public void LoadsAndAddsItsPrivacyMenu()
    {
        Assert.True(_host.LoadErrors.Count == 0, string.Join(Environment.NewLine, _host.LoadErrors));
        Assert.Single(_host.ShimAutoTampers);

        var items = PrivacyMenu.MenuItems;
        Assert.Equal(new[] { "&Enabled", "&Rename P3P header if invalid" }, items.Select(i => i.Text).ToArray());
        Assert.True(items[0].Checked);
        Assert.True(items[1].Enabled);
    }

    [Fact]
    public void AddsItsPrivacyInfoColumnBoundToAFlag()
    {
        var column = Assert.Single(FiddlerApplication.UI.lvSessions.Columns, c => c.Title == "Privacy Info");

        Assert.Equal(1, column.DisplayOrder);
        Assert.Equal(120, column.Width);
        Assert.Equal("X-Privacy", column.FlagName);
    }

    [Fact]
    public void TheMenuTurnsScanningOffAndRemembersIt()
    {
        var enabled = PrivacyMenu.MenuItems[0];

        enabled.PerformClick();

        Assert.False(enabled.Checked);
        Assert.False(PrivacyMenu.MenuItems[1].Enabled);
        Assert.False(_preferences.GetBoolPref(EnabledPref, true));

        var flags = Scan([("Set-Cookie", "a=1")]).Flags;
        Assert.Null(flags);
    }

    [Theory]
    [InlineData(null, "#FAFDA4", "Sets cookies without P3P")]
    [InlineData("CP=\"NOI DSP COR\"", "#ACDC85", "Sets cookies & P3P")]
    [InlineData("CP=\"PHY SAM\"", "#EC921A", "Sets cookies; P3P unsatisfactory for 3rd-party use")]
    public void ColoursResponsesThatSetCookies(string? p3p, string expectedColour, string expectedNote)
    {
        var headers = new List<(string Name, string Value)> { ("Set-Cookie", "a=1") };
        if (p3p is not null)
        {
            headers.Add(("P3P", p3p));
        }

        var flags = Scan(headers).Flags;

        Assert.NotNull(flags);
        Assert.Equal(expectedColour, flags!["ui-backcolor"]);
        Assert.Equal(expectedNote, flags["x-privacy"]);
    }

    [Fact]
    public void RenamesAMalformedP3PHeaderWhilePeeking()
    {
        var (flags, response) = Scan([("Set-Cookie", "a=1"), ("P3P", "CP=\"TOOLONGTOKEN\"")]);

        Assert.Equal("#E90A05", flags!["ui-backcolor"]);
        Assert.Equal("MALFORMED P3P: CP=\"TOOLONGTOKEN\"", flags["x-privacy"]);
        Assert.DoesNotContain(response.Headers, h => h.Name == "P3P");
        Assert.Contains(response.Headers, h => h.Name == "MALFORMED-P3P" && h.Value == "CP=\"TOOLONGTOKEN\"");
    }

    [Fact]
    public void AnExtensionInTwoFoldersLoadsOnceFromTheFirst()
    {
        // The app scans the user's own Extensions folder, then the one the
        // installer puts next to the app; the user's copy wins.
        var userFolder = Path.Combine(_folder, "Extensions");
        var installedFolder = Path.Combine(_folder, "Installed");
        Directory.CreateDirectory(installedFolder);
        File.Copy(FindPortedDll(), Path.Combine(installedFolder, PortedDllName));
        var log = new List<string>();

        var host = new ExtensionHost([userFolder, installedFolder], log: log.Add);
        try
        {
            host.Load();

            Assert.Single(host.ShimAutoTampers);
            Assert.Contains(log, line => line.StartsWith("Skipped " + Path.Combine(installedFolder, PortedDllName)));
        }
        finally
        {
            host.Unload();
        }
    }

    [Fact]
    public void LeavesResponsesWithoutCookiesAlone()
    {
        var (flags, _) = Scan([("Content-Type", "text/html")]);

        Assert.Null(flags);
    }

    /// <summary>Runs one exchange through the hooks the way the proxy does, returning the recorded flags and the response as sent.</summary>
    private (IReadOnlyDictionary<string, string>? Flags, CapturedResponse Response) Scan(IReadOnlyList<(string Name, string Value)> responseHeaders)
    {
        var session = _host.CreateSessionHost().BeginSession(1, "example.test", "https");
        var request = new CapturedRequest("GET", "/", "HTTP/1.1", [("Host", "example.test")], []);
        var response = new CapturedResponse("HTTP/1.1", 200, "OK", responseHeaders, []);

        session.PeekAtRequestHeaders(request);
        var result = session.RequestBefore(request);
        session.RequestAfter(result.Request);
        session.PeekAtResponseHeaders(result.Request, response);
        var sent = session.ResponseBefore(result.Request, response);
        session.ResponseAfter(result.Request, sent);
        return (session.Flags, sent);
    }

    private static string FindPortedDll()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, ".work", "bin", "PrivacyScanner", PortedDllName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            $"The ported {PortedDllName} wasn't found. Run tests/ExtensionPorts/port.ps1, then build or test this project (it builds the port first).");
    }
}
