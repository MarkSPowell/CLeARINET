using Clearinet.ProxyCore.SystemProxy;
using Xunit;

namespace Clearinet.ProxyCore.Tests;

/// <summary>
/// MacOSSystemProxy.Enable/Disable/RecoverFromCrash shell out to the real
/// <c>networksetup</c> CLI against the current user's actual network
/// services -- the same reason WinInetSystemProxyTests only exercises
/// WinInetSystemProxy.MergeBypassList rather than the registry-touching
/// methods. This sticks to the same pure, no-subprocess shape: the
/// argument-building methods (plain string construction, no OS
/// interaction) and the two output parsers, which are ordinary string
/// processing over a fixed, well-documented CLI output format -- no real
/// <c>networksetup</c> invocation needed to exercise either. See the
/// Interception Certificate Design doc's "Testing scope, deliberately
/// narrow" note.
/// </summary>
public class MacOSSystemProxyTests
{
    [Fact]
    public void BuildListAllNetworkServicesArguments_TakesNoParameters()
    {
        Assert.Equal(["-listallnetworkservices"], MacOSSystemProxy.BuildListAllNetworkServicesArguments());
    }

    [Fact]
    public void BuildGetProxyArguments_SelectsPlainOrSecureByTheSecureFlag()
    {
        Assert.Equal(["-getwebproxy", "Wi-Fi"], MacOSSystemProxy.BuildGetProxyArguments("Wi-Fi", secure: false));
        Assert.Equal(["-getsecurewebproxy", "Wi-Fi"], MacOSSystemProxy.BuildGetProxyArguments("Wi-Fi", secure: true));
    }

    [Fact]
    public void BuildSetProxyArguments_PointsTheGivenServiceAtTheHostAndPort()
    {
        Assert.Equal(
            ["-setwebproxy", "Wi-Fi", "127.0.0.1", "8888"],
            MacOSSystemProxy.BuildSetProxyArguments("Wi-Fi", "127.0.0.1", 8888, secure: false));
        Assert.Equal(
            ["-setsecurewebproxy", "Wi-Fi", "127.0.0.1", "8888"],
            MacOSSystemProxy.BuildSetProxyArguments("Wi-Fi", "127.0.0.1", 8888, secure: true));
    }

    [Fact]
    public void BuildSetProxyStateArguments_TogglesOnOrOffByTheEnabledFlag()
    {
        Assert.Equal(
            ["-setwebproxystate", "Wi-Fi", "on"],
            MacOSSystemProxy.BuildSetProxyStateArguments("Wi-Fi", enabled: true, secure: false));
        Assert.Equal(
            ["-setsecurewebproxystate", "Wi-Fi", "off"],
            MacOSSystemProxy.BuildSetProxyStateArguments("Wi-Fi", enabled: false, secure: true));
    }

    [Fact]
    public void ParseNetworkServiceNames_SkipsTheHeaderLineAndDisabledServices()
    {
        var output = "An asterisk (*) denotes that a network service is disabled.\n" +
                     "Wi-Fi\n" +
                     "*Thunderbolt Bridge\n" +
                     "Ethernet\n";

        Assert.Equal(["Wi-Fi", "Ethernet"], MacOSSystemProxy.ParseNetworkServiceNames(output));
    }

    [Fact]
    public void ParseNetworkServiceNames_ReturnsEmptyWhenEveryServiceIsDisabled()
    {
        var output = "An asterisk (*) denotes that a network service is disabled.\n" +
                     "*Wi-Fi\n" +
                     "*Ethernet\n";

        Assert.Empty(MacOSSystemProxy.ParseNetworkServiceNames(output));
    }

    [Fact]
    public void ParseProxyState_ParsesAnEnabledProxyWithAServerAndPort()
    {
        var output = "Enabled: Yes\nServer: 127.0.0.1\nPort: 8888\nAuthenticated Proxy Enabled: 0\n";

        var state = MacOSSystemProxy.ParseProxyState(output);

        Assert.True(state.Enabled);
        Assert.Equal("127.0.0.1", state.Server);
        Assert.Equal(8888, state.Port);
    }

    [Fact]
    public void ParseProxyState_ParsesNothingConfiguredAsANullServerAndPort()
    {
        var output = "Enabled: No\nServer: \nPort: 0\nAuthenticated Proxy Enabled: 0\n";

        var state = MacOSSystemProxy.ParseProxyState(output);

        Assert.False(state.Enabled);
        Assert.Null(state.Server);
        Assert.Null(state.Port);
    }

    [Fact]
    public void ParseProxyState_TreatsAConfiguredServerWithAPortOfZeroAsNotConfigured()
    {
        // Shouldn't happen in practice against real networksetup output,
        // but Port: 0 is indistinguishable from "no port at all" in this
        // format -- RestoreServiceBackup can't safely re-apply a port-0
        // proxy, so this is treated the same as nothing having been
        // configured before rather than risking a garbage restore.
        var output = "Enabled: No\nServer: 127.0.0.1\nPort: 0\nAuthenticated Proxy Enabled: 0\n";

        var state = MacOSSystemProxy.ParseProxyState(output);

        Assert.Null(state.Server);
        Assert.Null(state.Port);
    }

    [Fact]
    public void ParseProxyState_IsCaseInsensitiveForTheEnabledValue()
    {
        // Not observed in real networksetup output as far as this session
        // could confirm without a Mac to check against -- defensive rather
        // than a documented quirk being worked around.
        var output = "Enabled: yes\nServer: 10.0.0.1\nPort: 3128\n";

        Assert.True(MacOSSystemProxy.ParseProxyState(output).Enabled);
    }
}
