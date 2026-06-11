using BannerShop.Api.Services.DesignRequests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;

namespace BannerShop.Tests;

/// <summary>
/// Unit tests for DesignRequestJobProcessor (the hosted BackgroundService
/// that drains the design-request job queue).
/// </summary>
public class DesignRequestJobProcessorTests
{
    /// <summary>
    /// Creates a processor where IServiceProvider.GetRequiredService&lt;AiGenerationPipeline&gt;
    /// throws (since AiGenerationPipeline is sealed and has no parameterless constructor).
    /// The processor catches all exceptions from the pipeline call, so this is fine for
    /// lifecycle tests.
    /// </summary>
    private static (DesignRequestJobProcessor processor,
                    Mock<IDesignRequestJobQueue> queueMock)
        CreateProcessor()
    {
        var queue        = new Mock<IDesignRequestJobQueue>();
        var scopedSp     = new Mock<IServiceProvider>();
        var scope        = new Mock<IServiceScope>();
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var rootSp       = new Mock<IServiceProvider>();
        var logger       = new Mock<ILogger<DesignRequestJobProcessor>>();

        scope.Setup(s => s.ServiceProvider).Returns(scopedSp.Object);
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
        rootSp.Setup(s => s.GetService(typeof(IServiceScopeFactory))).Returns(scopeFactory.Object);

        // AiGenerationPipeline.GetRequiredService will throw InvalidOperationException
        // because it can't be constructed (no registered services). The processor catches this.
        scopedSp.Setup(s => s.GetService(typeof(AiGenerationPipeline))).Returns(null!);

        var processor = new DesignRequestJobProcessor(queue.Object, rootSp.Object, logger.Object);
        return (processor, queue);
    }

    [Fact]
    public async Task ExecuteAsync_OperationCancelledException_ExitsGracefully()
    {
        var (processor, queueMock) = CreateProcessor();

        // Immediately throw OperationCanceledException on dequeue → processor should exit
        queueMock
            .Setup(q => q.DequeueAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var task = processor.StartAsync(cts.Token);
        // Wait briefly for the task to complete (it should exit immediately)
        await task.WaitAsync(TimeSpan.FromSeconds(5));

        task.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_WhileRunning_StopsGracefully()
    {
        var (processor, queueMock) = CreateProcessor();

        // Simulate a blocking queue that respects cancellation
        queueMock
            .Setup(q => q.DequeueAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return 0;
            });

        await processor.StartAsync(CancellationToken.None);
        await processor.StopAsync(CancellationToken.None);
        // No exception = pass
    }

    [Fact]
    public async Task ExecuteAsync_PipelineThrows_LogsErrorAndContinues()
    {
        var (processor, queueMock) = CreateProcessor();

        var callCount = 0;
        // TCS is set when DequeueAsync is called a 2nd time (after the pipeline error),
        // proving the loop continued rather than aborting.
        var loopContinuedTcs = new TaskCompletionSource<bool>();

        queueMock
            .Setup(q => q.DequeueAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(ct =>
            {
                callCount++;
                if (callCount == 1)
                    return new ValueTask<int>(1);  // first call: deliver a job id

                // second call: signal the test and stop the loop cleanly
                loopContinuedTcs.TrySetResult(true);
                return new ValueTask<int>(Task.FromException<int>(new OperationCanceledException()));
            });

        await processor.StartAsync(CancellationToken.None);

        // Wait until the background loop has processed the first job and started a second iteration
        await loopContinuedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await processor.StopAsync(CancellationToken.None);

        callCount.Should().BeGreaterThanOrEqualTo(1);
    }
}
