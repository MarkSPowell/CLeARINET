using System.Text;
using Clearinet.Compatibility.Extensions;
using Clearinet.ProxyCore.Extensions;
using Clearinet.ProxyCore.Http;
using Clearinet.ProxyCore.Preferences;
using Xunit;
using CompatShimHost = Clearinet.CompatShim.CompatShimHost;

namespace Clearinet.ExtensionPorts.Tests;

/// <summary>
/// The CSP Rule Collector (David Risney's, MIT), built from its
/// CLeARINET-only fork (MarkSPowell/CSP-CLeARINET-Extension, see ../port.ps1),
/// loaded through
/// <see cref="ExtensionHost"/> the way the app loads it, and driven through
/// the same per-request hook session the proxy uses
/// (<see cref="IExtensionSessionHost"/>). No UI and no network: the report
/// host is answered by the extension itself.
///
/// The reports here are synthetic, written for this project.
///
/// Only compiled when the fork's source is present (see the test project
/// file), since port.ps1 may not have fetched it. These tests change
/// <see cref="CompatShimHost"/>'s static hooks, so they all live in this one
/// class and put the hooks back afterwards.
/// </summary>
public sealed class CspRuleCollectorPortTests : IDisposable
{
    private const string PortedDllName = "CLeARINETCSP.dll";
    private const string ReportHost = "fiddlercsp.deletethis.net";
    private const string EnabledPref = "ClearinetCSP.enabled";
    private const string VerbosePref = "ClearinetCSP.verboseLogging";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "clearinet-port-tests", Guid.NewGuid().ToString("N"));
    private readonly List<string> _log = [];
    private readonly Action<string> _originalLog = CompatShimHost.Log;
    private readonly IPreferenceStore _originalPreferences = CompatShimHost.Preferences;
    private readonly IPreferenceStore _preferences = PreferenceStore.CreateInMemory();
    private readonly ExtensionHost _host;

    public CspRuleCollectorPortTests()
    {
        CompatShimHost.Log = message => { lock (_log) { _log.Add(message); } };
        CompatShimHost.Preferences = _preferences;
        _preferences.SetBoolPref(EnabledPref, true);
        _preferences.SetBoolPref(VerbosePref, true);

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
            // The collectible load context may still hold the .dll open on
            // Windows for a moment; a leftover temp folder is harmless.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void LoadsThroughExtensionHostAsAPortedAutoTamper()
    {
        Assert.True(_host.LoadErrors.Count == 0, string.Join(Environment.NewLine, _host.LoadErrors));
        Assert.Single(_host.ShimAutoTampers);
        Assert.Single(_host.ShimExtensions);
        Assert.True(_host.CreateSessionHost().IsActive);
    }

    [Fact]
    public void AddsReportOnlyPoliciesToResponses()
    {
        var session = _host.CreateSessionHost().BeginSession(1, "example.test", "https");
        var request = Request("GET", "example.test", "/");
        session.PeekAtRequestHeaders(request);
        var result = session.RequestBefore(request);
        Assert.Null(result.LocalResponse);

        var response = session.ResponseBefore(result.Request, new CapturedResponse(
            "HTTP/1.1", 200, "OK", [("Content-Type", "text/html"), ("Cache-Control", "max-age=600")], Encoding.UTF8.GetBytes("<html></html>")));

        var policies = response.Headers.Where(h => h.Name == "Content-Security-Policy-Report-Only").Select(h => h.Value).ToList();
        Assert.Equal(2, policies.Count);
        Assert.Contains(policies, p => p.EndsWith($"report-uri https://{ReportHost}/unsafe-inline", StringComparison.Ordinal));
        Assert.Contains(policies, p => p.EndsWith($"report-uri https://{ReportHost}/unsafe-eval", StringComparison.Ordinal));
        Assert.Equal("private, max-age=0, no-cache", Assert.Single(response.Headers, h => h.Name == "Cache-Control").Value);
        Assert.Equal("<html></html>", Encoding.UTF8.GetString(response.Body));
    }

    [Fact]
    public void AnswersReportsItselfAndBuildsAPolicyFromThem()
    {
        var sessions = _host.CreateSessionHost();

        var first = SendReport(sessions, "/unsafe-inline", blockedUri: "https://cdn.example.test/app.js");
        Assert.NotNull(first.LocalResponse);
        Assert.Equal(200, first.LocalResponse!.StatusCode);
        Assert.Contains("Report received", Encoding.UTF8.GetString(first.LocalResponse.Body));
        Assert.Contains(first.LocalResponse.Headers, h => h.Name == "Content-Type" && h.Value == "text/html");

        // A blank blocked-uri sent to /unsafe-eval means eval() was used.
        SendReport(sessions, "/unsafe-eval", blockedUri: "");

        var total = LastLogLineContaining("Total");
        Assert.Contains("https://example.test/: Content-Security-Policy: default-src 'none'; script-src", total);
        Assert.Contains("cdn.example.test", total);
        Assert.Contains("'unsafe-eval'", total);
    }

    [Fact]
    public void FlagsSetInOneHookAreRecordedWithTheSession()
    {
        var sessions = _host.CreateSessionHost();
        var session = sessions.BeginSession(1, ReportHost, "https");
        var request = Request("POST", ReportHost, "/unsafe-inline", Report("https://cdn.example.test/app.js"));
        session.PeekAtRequestHeaders(request with { Body = [] });
        var result = session.RequestBefore(request);
        session.ResponseBefore(result.Request, result.LocalResponse!);

        Assert.NotNull(session.Flags);
        Assert.Equal("CSPReportGenerator", session.Flags!["ui-strikeout"]);
    }

    [Fact]
    public void LeavesTrafficAloneWhenCollectionIsOff()
    {
        _preferences.SetBoolPref(EnabledPref, false);
        var session = _host.CreateSessionHost().BeginSession(1, "example.test", "https");
        var request = Request("GET", "example.test", "/");
        var result = session.RequestBefore(request);
        var original = new CapturedResponse("HTTP/1.1", 200, "OK", [("Content-Type", "text/html")], []);

        var response = session.ResponseBefore(result.Request, original);

        Assert.DoesNotContain(response.Headers, h => h.Name == "Content-Security-Policy-Report-Only");
        Assert.Null(session.Flags);
    }

    private static ExtensionRequestResult SendReport(IExtensionSessionHost sessions, string path, string blockedUri)
    {
        var session = sessions.BeginSession(1, ReportHost, "https");
        var request = Request("POST", ReportHost, path, Report(blockedUri));
        session.PeekAtRequestHeaders(request with { Body = [] });
        var result = session.RequestBefore(request);
        if (result.LocalResponse is not null)
        {
            var response = session.ResponseBefore(result.Request, result.LocalResponse);
            session.ResponseAfter(result.Request, response);
        }

        return result;
    }

    private static string Report(string blockedUri) =>
        "{\"csp-report\":{\"document-uri\":\"https://example.test/\",\"referrer\":\"\"," +
        "\"violated-directive\":\"script-src 'none'\",\"effective-directive\":\"script-src\"," +
        $"\"original-policy\":\"script-src 'none'\",\"blocked-uri\":\"{blockedUri}\",\"status-code\":200}}}}";

    private static CapturedRequest Request(string method, string host, string path, string? body = null)
    {
        byte[] bytes = body is null ? [] : Encoding.UTF8.GetBytes(body);
        var headers = new List<(string Name, string Value)> { ("Host", host) };
        if (body is not null)
        {
            headers.Add(("Content-Type", "application/csp-report"));
            headers.Add(("Content-Length", bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return new CapturedRequest(method, path, "HTTP/1.1", headers, bytes);
    }

    private string LastLogLineContaining(string text)
    {
        lock (_log)
        {
            return _log.LastOrDefault(line => line.Contains(text, StringComparison.Ordinal))
                ?? throw new Xunit.Sdk.XunitException($"No log line contains '{text}'. Log:{Environment.NewLine}{string.Join(Environment.NewLine, _log)}");
        }
    }

    /// <summary>The newest CLeARINETCSP.dll the fork's own build produced under .work/CLeARINETCSP/bin/.</summary>
    private static string FindPortedDll()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var bin = Path.Combine(directory.FullName, ".work", "CLeARINETCSP", "bin");
            if (!Directory.Exists(bin))
            {
                continue;
            }

            var newest = Directory.EnumerateFiles(bin, PortedDllName, SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (newest is not null)
            {
                return newest;
            }
        }

        throw new FileNotFoundException(
            $"{PortedDllName} wasn't found under tests/ExtensionPorts/.work/CLeARINETCSP/bin/. Run tests/ExtensionPorts/port.ps1 (with -CspSource until a commit is pinned), then build or test this project.");
    }
}
