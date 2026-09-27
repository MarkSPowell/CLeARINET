using Clearinet.ProxyCore.Http;
using Clearinet.ProxyCore.Sessions;
using Xunit;

namespace Clearinet.ProxyCore.Tests;

/// <summary>Removing sessions (Edit > Remove Selected Session / Remove All Sessions).</summary>
public class SessionStoreRemovalTests
{
    private static Session AddOne(SessionStore store) =>
        store.Add(
            "example.test",
            DateTimeOffset.UnixEpoch,
            new CapturedRequest("GET", "/", "HTTP/1.1", [], []),
            new CapturedResponse("HTTP/1.1", 200, "OK", [], []));

    [Fact]
    public void RemovesOnlyTheGivenSessionsAndReportsThem()
    {
        var store = new SessionStore();
        var first = AddOne(store);
        var second = AddOne(store);
        var third = AddOne(store);
        var reported = new List<int>();
        store.SessionsRemoved += ids => reported.AddRange(ids);

        Assert.Equal(2, store.Remove([first.Id, third.Id, 999]));

        Assert.Equal(new[] { second.Id }, store.Snapshot().Select(s => s.Id).ToArray());
        Assert.Equal(new[] { first.Id, third.Id }, reported.ToArray());
    }

    [Fact]
    public void ClearRemovesEverythingButIdsKeepCounting()
    {
        var store = new SessionStore();
        AddOne(store);
        var last = AddOne(store);
        var reported = 0;
        store.SessionsRemoved += ids => reported += ids.Count;

        Assert.Equal(2, store.Clear());
        Assert.Empty(store.Snapshot());
        Assert.Equal(2, reported);

        Assert.Equal(last.Id + 1, AddOne(store).Id);
    }

    [Fact]
    public void RemovingNothingRaisesNothing()
    {
        var store = new SessionStore();
        var raised = false;
        store.SessionsRemoved += _ => raised = true;

        Assert.Equal(0, store.Clear());
        Assert.Equal(0, store.Remove([1, 2]));
        Assert.False(raised);
    }
}
