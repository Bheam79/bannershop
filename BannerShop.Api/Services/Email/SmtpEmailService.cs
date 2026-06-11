using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace BannerShop.Api.Services.Email;

/// <summary>
/// SMTP-backed implementation of <see cref="IEmailService"/>. Uses
/// <see cref="System.Net.Mail.SmtpClient"/> with TLS (EnableSsl=true) and
/// <see cref="NetworkCredential"/> auth when SmtpUser/SmtpPass are configured.
///
/// Registered by <c>Program.cs</c> when <c>Email:SmtpHost</c> is set; otherwise
/// <see cref="NullEmailService"/> is used so dev/test runs don't try to dial out.
///
/// Per BANNERSH-58: SMTP AUTH is always required in this codebase (confirmed by
/// the project lead) — empty credentials still try to connect but will fail at
/// the server, which is intentional so misconfiguration is caught loudly.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Thin SMTP wrapper — tested via integration against a real SMTP server")]
public sealed class SmtpEmailService : IEmailService
{
    private readonly EmailOptions _opts;
    private readonly ILogger<SmtpEmailService> _log;

    public SmtpEmailService(IOptions<EmailOptions> opts, ILogger<SmtpEmailService> log)
    {
        _opts = opts.Value;
        _log = log;
    }

    public async Task SendAsync(string to, string subject, string bodyHtml, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(to))
            throw new ArgumentException("Recipient address is required.", nameof(to));
        if (string.IsNullOrWhiteSpace(_opts.SmtpHost))
            throw new InvalidOperationException("SMTP host is not configured.");
        if (string.IsNullOrWhiteSpace(_opts.From))
            throw new InvalidOperationException("Email 'From' address is not configured.");

        using var message = new MailMessage
        {
            From = new MailAddress(_opts.From),
            Subject = subject ?? string.Empty,
            Body = bodyHtml ?? string.Empty,
            IsBodyHtml = true,
            BodyEncoding = System.Text.Encoding.UTF8,
            SubjectEncoding = System.Text.Encoding.UTF8,
        };
        message.To.Add(new MailAddress(to));

        using var client = new SmtpClient(_opts.SmtpHost, _opts.SmtpPort)
        {
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            // SMTP AUTH is always required per project decision; credentials come
            // straight from configuration.  An empty pair will provoke an auth
            // failure at the server, which surfaces a misconfiguration loudly
            // rather than silently dropping mail.
            Credentials = new NetworkCredential(_opts.SmtpUser, _opts.SmtpPass),
        };

        try
        {
            // SmtpClient.SendMailAsync(MailMessage, CancellationToken) was added
            // in .NET 5; use it so the host's shutdown token actually aborts
            // long-running sends.
            await client.SendMailAsync(message, ct);
            _log.LogInformation("Email sent to {To} (subject={Subject})", to, subject);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to send email to {To} (subject={Subject})", to, subject);
            throw;
        }
    }
}
