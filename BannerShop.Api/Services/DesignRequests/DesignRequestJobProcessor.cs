using Microsoft.Extensions.Hosting;

namespace BannerShop.Api.Services.DesignRequests;

/// <summary>
/// Long-running hosted service that drains the design-request job queue and
/// invokes <see cref="AiGenerationPipeline"/> for each id.
///
/// Single-threaded by design (gpt-image-2 is rate-limited and the v1 volume is
/// tiny). Parallelism can be added by spinning up multiple readers once the
/// channel pattern is in place.
/// </summary>
public sealed class DesignRequestJobProcessor : BackgroundService
{
    private readonly IDesignRequestJobQueue _queue;
    private readonly IServiceProvider _sp;
    private readonly ILogger<DesignRequestJobProcessor> _log;

    public DesignRequestJobProcessor(
        IDesignRequestJobQueue queue,
        IServiceProvider sp,
        ILogger<DesignRequestJobProcessor> log)
    {
        _queue = queue;
        _sp = sp;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("DesignRequestJobProcessor started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            int id;
            try
            {
                id = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }

            try
            {
                using var scope = _sp.CreateScope();
                var pipeline = scope.ServiceProvider.GetRequiredService<AiGenerationPipeline>();
                await pipeline.RunAsync(id, stoppingToken);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "DesignRequestJobProcessor: unhandled error for id {Id}", id);
            }
        }
        _log.LogInformation("DesignRequestJobProcessor stopped.");
    }
}
