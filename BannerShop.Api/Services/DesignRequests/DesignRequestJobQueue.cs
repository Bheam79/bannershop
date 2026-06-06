using System.Threading.Channels;

namespace BannerShop.Api.Services.DesignRequests;

/// <summary>
/// Bounded, single-consumer in-process channel queue.
/// </summary>
public sealed class DesignRequestJobQueue : IDesignRequestJobQueue
{
    private readonly Channel<int> _channel;

    public DesignRequestJobQueue()
    {
        _channel = Channel.CreateBounded<int>(new BoundedChannelOptions(capacity: 256)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public ValueTask EnqueueAsync(int designRequestId, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(designRequestId, ct);

    public ValueTask<int> DequeueAsync(CancellationToken ct)
        => _channel.Reader.ReadAsync(ct);
}
