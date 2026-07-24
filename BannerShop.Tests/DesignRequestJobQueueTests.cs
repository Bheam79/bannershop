using System.Collections.Concurrent;
using BannerShop.Api.Services.DesignRequests;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests;

/// <summary>
/// Tests for <see cref="DesignRequestJobQueue"/> — the in-process producer/
/// consumer channel that backs the AI generation pipeline (BANNERSH-19). The
/// queue is a thin wrapper over a bounded single-reader/multi-writer
/// <see cref="System.Threading.Channels.Channel{T}"/>, so these tests lock down
/// the behaviour callers rely on: FIFO delivery, a Dequeue that blocks until an
/// item arrives, cancellation propagation, and no item loss when many producers
/// enqueue concurrently against the single consumer.
/// </summary>
public class DesignRequestJobQueueTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task EnqueueThenDequeue_ReturnsSameId()
    {
        var queue = new DesignRequestJobQueue();

        await queue.EnqueueAsync(42);
        var id = await queue.DequeueAsync(CancellationToken.None);

        id.Should().Be(42);
    }

    [Fact]
    public async Task Dequeue_ReturnsItemsInFifoOrder()
    {
        var queue = new DesignRequestJobQueue();

        for (var i = 1; i <= 5; i++)
            await queue.EnqueueAsync(i);

        var drained = new List<int>();
        for (var i = 0; i < 5; i++)
            drained.Add(await queue.DequeueAsync(CancellationToken.None));

        drained.Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public async Task Dequeue_WhenEmpty_BlocksUntilItemEnqueued()
    {
        var queue = new DesignRequestJobQueue();

        var dequeue = queue.DequeueAsync(CancellationToken.None).AsTask();

        // Nothing enqueued yet — the consumer must still be waiting.
        dequeue.IsCompleted.Should().BeFalse();

        await queue.EnqueueAsync(7);

        var completed = await Task.WhenAny(dequeue, Task.Delay(Timeout));
        completed.Should().BeSameAs(dequeue, "the pending dequeue should complete once an item is enqueued");
        (await dequeue).Should().Be(7);
    }

    [Fact]
    public async Task Dequeue_WithAlreadyCancelledToken_Throws()
    {
        var queue = new DesignRequestJobQueue();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await queue.DequeueAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Dequeue_WhenTokenCancelledWhileWaiting_Throws()
    {
        var queue = new DesignRequestJobQueue();
        using var cts = new CancellationTokenSource();

        var dequeue = queue.DequeueAsync(cts.Token).AsTask();
        dequeue.IsCompleted.Should().BeFalse();

        cts.Cancel();

        var act = async () => await dequeue;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Enqueue_ManyConcurrentProducers_SingleConsumerDrainsAllWithoutLoss()
    {
        var queue = new DesignRequestJobQueue();
        const int producerCount = 8;
        const int perProducer = 20;
        const int total = producerCount * perProducer;

        var producers = Enumerable.Range(0, producerCount).Select(p => Task.Run(async () =>
        {
            for (var i = 0; i < perProducer; i++)
                await queue.EnqueueAsync(p * perProducer + i);
        }));

        var received = new ConcurrentBag<int>();
        var consumer = Task.Run(async () =>
        {
            for (var i = 0; i < total; i++)
                received.Add(await queue.DequeueAsync(CancellationToken.None));
        });

        var all = Task.WhenAll(producers.Append(consumer));
        var completed = await Task.WhenAny(all, Task.Delay(Timeout));
        completed.Should().BeSameAs(all, "all producers and the consumer should finish within the timeout");
        await all;

        received.Should().HaveCount(total);
        received.Distinct().Should().HaveCount(total, "every enqueued id must be delivered exactly once");
    }
}
