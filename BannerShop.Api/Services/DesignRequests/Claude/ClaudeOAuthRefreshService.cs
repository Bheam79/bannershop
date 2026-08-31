namespace BannerShop.Api.Services.DesignRequests.Claude;

/// <summary>Periodically rotates Claude's short-lived access token.</summary>
public sealed class ClaudeOAuthRefreshService : BackgroundService
{
    private readonly ClaudeOAuthTokenManager _tokens;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ClaudeOAuthRefreshService> _log;

    public ClaudeOAuthRefreshService(
        ClaudeOAuthTokenManager tokens,
        TimeProvider timeProvider,
        ILogger<ClaudeOAuthRefreshService> log)
    {
        _tokens = tokens;
        _timeProvider = timeProvider;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _tokens.RefreshIfNeededAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Keep the current token set and retry on the next tick. A request
                // also performs the same expiry check before invoking Claude CLI.
                _log.LogWarning(ex, "Claude OAuth background refresh failed; will retry.");
            }

            var interval = TimeSpan.FromMinutes(Math.Max(
                1,
                _tokens.RefreshIntervalMinutes));
            await Task.Delay(interval, _timeProvider, stoppingToken);
        }
    }
}
