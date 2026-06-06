namespace BannerShop.Api.Services.DesignRequests;

/// <summary>
/// In-process producer/consumer queue for AI generation jobs. Producers (webhook
/// handler, service layer) enqueue a design-request id; a single
/// <see cref="DesignRequestJobProcessor"/> hosted service drains the queue and
/// runs the pipeline.
///
/// In-process is fine for v1 volumes (a handful of paid requests per day). When
/// load demands it this can be swapped for Hangfire / Azure Queue without
/// changing callers.
/// </summary>
public interface IDesignRequestJobQueue
{
    /// <summary>Enqueues the given request id for AI processing.</summary>
    ValueTask EnqueueAsync(int designRequestId, CancellationToken ct = default);

    /// <summary>Pulls the next job id, awaiting one if the queue is empty.</summary>
    ValueTask<int> DequeueAsync(CancellationToken ct);
}
