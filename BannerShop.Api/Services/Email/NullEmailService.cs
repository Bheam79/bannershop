namespace BannerShop.Api.Services.Email;

/// <summary>
/// No-op email service used when SMTP is not configured (dev / test environments).
/// All calls are silently discarded.
/// </summary>
public sealed class NullEmailService : IEmailService
{
    private readonly ILogger<NullEmailService> _log;

    public NullEmailService(ILogger<NullEmailService> log) => _log = log;

    public Task SendAsync(string to, string subject, string bodyHtml, CancellationToken ct = default)
    {
        _log.LogDebug("NullEmailService: suppressed email to {To} — subject: {Subject}", to, subject);
        return Task.CompletedTask;
    }
}
