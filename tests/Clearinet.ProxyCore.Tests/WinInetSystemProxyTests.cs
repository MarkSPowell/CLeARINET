using Clearinet.ProxyCore.SystemProxy;
using Xunit;

namespace Clearinet.ProxyCore.Tests;

/// <summary>
/// WinInetSystemProxy's Enable/Disable/RecoverFromCrash mutate the current
/// user's real WinINET registry settings -- exactly the ones controlling
/// whatever proxy the developer running this test suite is actually using.
/// A unit test that called them for real (even to restore its own changes
/// afterward) would risk leaving a real machine's internet connectivity
/// broken if the test failed partway through, which is a much worse
/// outcome than a gap in coverage. So only the one pure, side-effect-free
/// piece of that class is exercised here; the registry-touching methods
/// are left to manual verification (see the Project Plan's system-proxy
/// gap entry).
/// </summary>
public class WinInetSystemProxyTests
{
    [Fact]
    public void MergeBypassList_AddsLocalWhenTheExistingListIsEmpty()
    {
        Assert.Equal("<local>", WinInetSystemProxy.MergeBypassList(null));
        Assert.Equal("<local>", WinInetSystemProxy.MergeBypassList(string.Empty));
    }

    [Fact]
    public void MergeBypassList_AppendsLocalToAnExistingListThatLacksIt()
    {
        Assert.Equal("*.corp.example.com;<local>", WinInetSystemProxy.MergeBypassList("*.corp.example.com"));
    }

    [Fact]
    public void MergeBypassList_LeavesAnExistingListThatAlreadyHasLocalUnchanged()
    {
        Assert.Equal("*.corp.example.com;<local>", WinInetSystemProxy.MergeBypassList("*.corp.example.com;<local>"));
    }

    [Fact]
    public void MergeBypassList_MatchesLocalCaseInsensitively()
    {
        // WinINET's own bypass-list matching for this token is case
        // insensitive, so a differently-cased entry someone (or another
        // tool) already added still counts as "already present" -- this
        // shouldn't grow a second, redundant entry.
        Assert.Equal("*.corp.example.com;<LOCAL>", WinInetSystemProxy.MergeBypassList("*.corp.example.com;<LOCAL>"));
    }
}
