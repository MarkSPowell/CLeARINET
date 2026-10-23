using Violens.ProxyCore.SystemProxy;
using Xunit;

namespace Violens.ProxyCore.Tests;

/// <summary>
/// Enable, Disable and RecoverFromCrash change the real registry settings
/// of whoever runs the tests, and a failure midway could break their
/// connection, so only <c>MergeBypassList</c> is tested here.
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
