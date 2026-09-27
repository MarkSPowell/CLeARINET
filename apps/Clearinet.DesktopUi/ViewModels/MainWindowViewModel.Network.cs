using Clearinet.ProxyCore.Proxy;
using Clearinet.ProxyCore.SystemProxy;

namespace Clearinet.DesktopUi.ViewModels;

/// <summary>
/// Tools &gt; Connections: forwarding through the network's own proxy
/// (<see cref="UpstreamProxy"/>), and letting phones and other computers
/// use CLeARINET as their proxy.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private const string DirectSetting = "none";

    private bool _showConnectionsRow;
    private string _upstreamProxySetting = string.Empty;
    private bool _allowRemoteClients;
    private string _gatewayStatus = "Upstream proxy: decided when capture starts.";
    private UpstreamProxyDetection? _detectedUpstream;

    /// <summary>Whether the Connections row shows (Tools &gt; Connections). Purely a visibility switch.</summary>
    public bool ShowConnectionsRow
    {
        get => _showConnectionsRow;
        set
        {
            if (SetField(ref _showConnectionsRow, value))
            {
                _preferences.SetBoolPref(PreferenceKeys.ShowConnectionsRow, value);
            }
        }
    }

    /// <summary>
    /// The upstream proxy: empty for automatic (whatever the system proxy
    /// was before CLeARINET took over), "none" to connect directly, or
    /// "host:port". Applies immediately while capturing.
    /// </summary>
    public string UpstreamProxySetting
    {
        get => _upstreamProxySetting;
        set
        {
            if (SetField(ref _upstreamProxySetting, value ?? string.Empty))
            {
                _preferences.SetStringPref(PreferenceKeys.UpstreamProxy, _upstreamProxySetting);
                if (_proxy is not null)
                {
                    ApplyGateway(_proxy);
                }
            }
        }
    }

    /// <summary>
    /// Fiddler Classic's "Allow remote computers to connect": listen on every
    /// network interface, not just this machine, so a phone can use
    /// CLeARINET as its proxy. Takes effect the next time capture starts.
    /// </summary>
    public bool AllowRemoteClients
    {
        get => _allowRemoteClients;
        set
        {
            if (SetField(ref _allowRemoteClients, value))
            {
                _preferences.SetBoolPref(PreferenceKeys.AllowRemoteClients, value);
                RaisePropertyChanged(nameof(RemoteClientsHint));
            }
        }
    }

    /// <summary>Which upstream proxy is in use, for the Connections row.</summary>
    public string GatewayStatus
    {
        get => _gatewayStatus;
        private set => SetField(ref _gatewayStatus, value);
    }

    /// <summary>What to set on a phone, shown next to the remote-computers checkbox.</summary>
    public string RemoteClientsHint
    {
        get
        {
            if (!AllowRemoteClients)
            {
                return string.Empty;
            }

            if (_proxy is not { AllowsRemoteClients: true })
            {
                return "Takes effect when capture starts.";
            }

            var addresses = LocalNetwork.GetIPv4Addresses();
            var where = addresses.Count == 0
                ? $"this computer's address, port {_proxy.Port}"
                : string.Join(" or ", addresses.Select(a => $"{a}:{_proxy.Port}"));
            return $"On the other device, set the proxy to {where}, then open http://{ProxyHomePage.HostName}/ to install the certificate.";
        }
    }

    private void LoadNetworkPreferences()
    {
        _showConnectionsRow = _preferences.GetBoolPref(PreferenceKeys.ShowConnectionsRow, false);
        _upstreamProxySetting = _preferences.GetStringPref(PreferenceKeys.UpstreamProxy, string.Empty);
        _allowRemoteClients = _preferences.GetBoolPref(PreferenceKeys.AllowRemoteClients, false);
    }

    /// <summary>
    /// Called by Start once the listener is running and before CLeARINET
    /// registers as the system proxy (so the system setting read is still the
    /// network's own). Returns a phrase for the status line.
    /// </summary>
    private string StartGateway(InterceptingProxyListener listener)
    {
        _detectedUpstream = SystemProxyController.DetectUpstream(listener.Port);
        var status = ApplyGateway(listener);
        RaisePropertyChanged(nameof(RemoteClientsHint));
        return status;
    }

    /// <summary>Sets the listener's gateway from <see cref="UpstreamProxySetting"/> and returns what it chose.</summary>
    private string ApplyGateway(InterceptingProxyListener listener)
    {
        var setting = UpstreamProxySetting.Trim();
        string description;
        if (setting.Length == 0)
        {
            var detected = _detectedUpstream ?? UpstreamProxyDetection.None;
            listener.Gateway = detected.Proxy;
            description = detected.Description;
        }
        else if (string.Equals(setting, DirectSetting, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(setting, "direct", StringComparison.OrdinalIgnoreCase))
        {
            listener.Gateway = null;
            description = "connecting directly (upstream proxy set to none)";
        }
        else if (UpstreamProxy.Parse(setting) is { } manual)
        {
            if (manual.IsSelf(listener.Port))
            {
                listener.Gateway = null;
                description = $"not forwarding through {manual}, which is CLeARINET itself; connecting directly";
            }
            else
            {
                listener.Gateway = manual;
                description = $"forwarding through {manual}";
            }
        }
        else
        {
            listener.Gateway = null;
            description = $"couldn't read the upstream proxy \"{setting}\" (use host:port, or none), so connecting directly";
        }

        GatewayStatus = "Upstream: " + description + ".";
        return description;
    }

    private void StopGateway()
    {
        _detectedUpstream = null;
        GatewayStatus = "Upstream proxy: decided when capture starts.";
        RaisePropertyChanged(nameof(RemoteClientsHint));
    }
}
