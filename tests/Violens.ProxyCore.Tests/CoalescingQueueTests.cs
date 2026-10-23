using Violens.ProxyCore.Sessions;
using Xunit;

namespace Violens.ProxyCore.Tests;

/// <summary>
/// <see cref="CoalescingQueue{T}"/>, which batches newly captured sessions
/// into one UI update per short interval. The scheduler here just holds on
/// to each callback, standing in for "later, on the UI thread".
/// </summary>
public class CoalescingQueueTests
{
    private sealed class Harness
    {
        public List<Action> Scheduled { get; } = [];

        public List<IReadOnlyList<int>> Delivered { get; } = [];

        public CoalescingQueue<int> Queue { get; }

        public Harness() => Queue = new CoalescingQueue<int>(Scheduled.Add, batch => Delivered.Add(batch.ToArray()));

        public void RunScheduled()
        {
            var due = Scheduled.ToArray();
            Scheduled.Clear();
            foreach (var callback in due)
            {
                callback();
            }
        }
    }

    [Fact]
    public void ABurstOfArrivalsIsOneScheduledDelivery()
    {
        var harness = new Harness();
        for (var i = 1; i <= 100; i++)
        {
            harness.Queue.Add(i);
        }

        Assert.Single(harness.Scheduled);
        Assert.Empty(harness.Delivered);

        harness.RunScheduled();

        var batch = Assert.Single(harness.Delivered);
        Assert.Equal(Enumerable.Range(1, 100), batch);
    }

    [Fact]
    public void ArrivalsAfterADeliveryScheduleTheNextOne()
    {
        var harness = new Harness();
        harness.Queue.Add(1);
        harness.RunScheduled();
        harness.Queue.Add(2);
        harness.Queue.Add(3);

        Assert.Single(harness.Scheduled);
        harness.RunScheduled();

        Assert.Equal([[1], [2, 3]], harness.Delivered);
    }

    [Fact]
    public void FlushDeliversNowAndLeavesTheScheduledCallbackNothingToDo()
    {
        var harness = new Harness();
        harness.Queue.Add(1);
        harness.Queue.Add(2);

        harness.Queue.Flush();
        Assert.Equal([[1, 2]], harness.Delivered);

        // Never an empty batch.
        harness.RunScheduled();
        harness.Queue.Flush();
        Assert.Single(harness.Delivered);
    }

    [Fact]
    public async Task KeepsEveryItemAddedFromManyThreads()
    {
        var delivered = new List<int>();
        var scheduled = new List<Action>();
        var queue = new CoalescingQueue<int>(
            callback => { lock (scheduled) { scheduled.Add(callback); } },
            batch => { lock (delivered) { delivered.AddRange(batch); } });

        await Task.WhenAll(Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++)
            {
                queue.Add((t * 1000) + i);
            }
        })));
        queue.Flush();

        Assert.Equal(Enumerable.Range(0, 8000), delivered.Order());
    }
}
