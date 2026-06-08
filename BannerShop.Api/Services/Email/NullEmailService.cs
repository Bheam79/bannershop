using Microsoft.Extensions.Hosting;

namespace BannerShop.Api.Services.Email;

/// <summary>
/// No-op email service used when SMTP is not configured (dev / test environments).
/// All calls are silently discarded.
///
/// Per BANNERSH-58: log level is <see cref="LogLevel.Debug"/> in Development
/// (where suppressed mail is expected and we don't want noise) but
/// <see cref="LogLevel.Warning"/> in any other environment, because in
/// Production / Staging a dropped email is almost always a misconfiguration
/// worth alerting on.
/// </summary>
public sealed class NullEmailService : IEmailService
{
    private readonly ILogger<NullEmailService> _log;
    private readonly bool _isDevelopment;

    public NullEmailService(ILogger<NullEmailService> log, IHostEnvironment env)
    {
        _log = log;
        _isDevelopment = env.IsDevelopment();
    }

    public Task SendAsync(string to, string subject, string bodyHtml, CancellationToken ct = default)
    {
        var level = _isDevelopment ? LogLevel.Debug : LogLevel.Warning;
        _log.Log(level,
            "NullEmailService: suppressed email to {To} — subject: {Subject}. " +
            "SMTP is not configured; set Email:SmtpHost to enable real delivery.",
            to, subject);
        return Task.CompletedTask;
    }
}
