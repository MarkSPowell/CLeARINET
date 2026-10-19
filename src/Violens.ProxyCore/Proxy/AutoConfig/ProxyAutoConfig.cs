using System.Collections.Concurrent;

namespace Violens.ProxyCore.Proxy.AutoConfig;

/// <summary>
/// Chooses the upstream proxy per request from the network's automatic
/// proxy configuration: a PAC script at a configured URL, or one found by
/// automatic detection (<see cref="WpadDiscovery"/>), as Windows' and
/// macOS' "Use setup script" and "Automatically detect settings" do.
///
/// The script is fetched in the background as soon as this is created;
/// requests that arrive first wait for it. If there's no script, or it
/// can't be fetched or run, the <c>fallback</c> proxy (the system's manual
/// proxy setting, if any) is used, or else a direct connection -- also what
/// Windows does. <see cref="Description"/> says which, and
/// <see cref="DescriptionChanged"/> fires when it changes.
/// </summary>
public sealed class ProxyAutoConfig : IDisposable
{
    private const int MaxCachedAnswers = 2000;
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(30);

    private readonly string? _scriptUrl;
    private readonly bool _autoDetect;
    private readonly UpstreamProxy? _fallback;
    private readonly IPacHost _host;
    private readonly CancellationTokenSource _disposed = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<UpstreamProxy?>> _answers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Task<PacScript?> _script;
    private string _description;

    private ProxyAutoConfig(string? scriptUrl, bool autoDetect, UpstreamProxy? fallback, IPacHost? host, Func<CancellationToken, Task<string>>? download)
    {
        _scriptUrl = scriptUrl;
        _autoDetect = autoDetect;
        _fallback = fallback;
        _host = host ?? SystemPacHost.Instance;
        _description = autoDetect
            ? "looking for automatic proxy settings"
            : $"loading the automatic proxy configuration script {scriptUrl}";
        _script = Task.Run(() => LoadAsync(download, _disposed.Token));
    }

    /// <summary>
    /// The system's automatic proxy configuration. With
    /// <paramref name="autoDetect"/>, the network is searched first (WPAD),
    /// then <paramref name="scriptUrl"/> is used if that finds nothing.
    /// </summary>
    public static ProxyAutoConfig Create(bool autoDetect, string? scriptUrl, UpstreamProxy? fallback, IPacHost? host = null) =>
        new(string.IsNullOrWhiteSpace(scriptUrl) ? null : scriptUrl.Trim(), autoDetect, fallback, host, download: null);

    /// <summary>A script given directly, as if downloaded from <paramref name="scriptUrl"/>. For tests.</summary>
    public static ProxyAutoConfig FromScript(string script, IPacHost? host = null, UpstreamProxy? fallback = null, string scriptUrl = "http://pac.test/proxy.pac") =>
        new(scriptUrl, autoDetect: false, fallback, host, _ => Task.FromResult(script));

    /// <summary>What's in use, for the status line, e.g. "using the automatic proxy configuration script http://wpad/wpad.dat".</summary>
    public string Description => Volatile.Read(ref _description);

    /// <summary>Fires (on a background thread) when <see cref="Description"/> changes.</summary>
    public event Action<string>? DescriptionChanged;

    /// <summary>Completes when the script has loaded, or failed to.</summary>
    public Task Loaded => _script;

    /// <summary>
    /// The routes to try for a request, in order: a proxy, or null for a
    /// direct connection. <paramref name="url"/> is what the script is
    /// given; for HTTPS, browsers pass only the scheme, host and port
    /// ("https://example.com/"), and so should callers.
    /// </summary>
    public async Task<IReadOnlyList<UpstreamProxy?>> GetRoutesAsync(string url, string host, CancellationToken cancellationToken)
    {
        var script = await _script.WaitAsync(cancellationToken);
        if (script is null)
        {
            return FallbackRoutes(host);
        }

        if (_answers.TryGetValue(url, out var cached))
        {
            return cached;
        }

        IReadOnlyList<UpstreamProxy?> routes;
        try
        {
            var answer = await Task.Run(() => script.FindProxyForUrl(url, host), cancellationToken);
            routes = PacResult.Parse(answer, out var unsupported);
            if (unsupported.Count > 0)
            {
                _host.Log($"{url}: skipped {string.Join("; ", unsupported)} (SOCKS and HTTPS proxies aren't supported yet)");
            }
        }
        catch (PacScriptException ex)
        {
            _host.Log(ex.Message);
            return FallbackRoutes(host);
        }

        if (_answers.Count >= MaxCachedAnswers)
        {
            _answers.Clear();
        }

        _answers[url] = routes;
        return routes;
    }

    /// <summary>Stops a download or detection still in progress.</summary>
    public void Dispose() => _disposed.Cancel();

    private IReadOnlyList<UpstreamProxy?> FallbackRoutes(string host) =>
        _fallback is null || _fallback.ShouldBypass(host) ? [null] : [_fallback];

    private string FallbackPhrase => _fallback is null ? "connecting directly" : $"forwarding through {_fallback}";

    private async Task<PacScript?> LoadAsync(Func<CancellationToken, Task<string>>? download, CancellationToken cancellationToken)
    {
        var url = _scriptUrl;
        try
        {
            if (_autoDetect)
            {
                var found = await WpadDiscovery.FindScriptUrlAsync(cancellationToken);
                if (found is not null)
                {
                    url = found;
                }
                else if (url is null)
                {
                    SetDescription($"automatic proxy detection found no configuration script, so {FallbackPhrase}");
                    return null;
                }
            }

            if (url is null)
            {
                SetDescription(FallbackPhrase);
                return null;
            }

            var source = download is not null
                ? await download(cancellationToken)
                : await DownloadAsync(url, cancellationToken);
            var script = new PacScript(source, _host);
            SetDescription($"using the automatic proxy configuration script {url}");
            return script;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            // An unreachable server, a 404, bad JavaScript: all mean the
            // same thing here -- carry on without the script, and say why.
            var reason = ex is PacScriptException ? ex.Message : ex.GetBaseException().Message;
            SetDescription($"couldn't use the automatic proxy configuration script {url} ({reason}), so {FallbackPhrase}");
            return null;
        }
    }

    /// <summary>
    /// Fetches the script directly, never through a proxy: while capturing,
    /// the system proxy is VioLens itself. file:// URLs are read from disk.
    /// </summary>
    private static async Task<string> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new PacScriptException($"\"{url}\" isn't a URL");
        }

        if (uri.IsFile)
        {
            return await File.ReadAllTextAsync(uri.LocalPath, cancellationToken);
        }

        using var handler = new HttpClientHandler { UseProxy = false };
        using var client = new HttpClient(handler) { Timeout = DownloadTimeout };
        using var response = await client.GetAsync(uri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new PacScriptException($"the server answered {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private void SetDescription(string description)
    {
        Volatile.Write(ref _description, description);
        DescriptionChanged?.Invoke(description);
    }
}
