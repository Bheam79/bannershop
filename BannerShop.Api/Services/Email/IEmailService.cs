namespace BannerShop.Api.Services.Email;

/// <summary>
/// Thin abstraction for outbound emails so the real SMTP implementation can be
/// swapped for a no-op in development / when not configured.
/// </summary>
public interface IEmailService
{
    /// <param name="to">Recipient email address.</param>
    /// <param name="subject">Email subject line.</param>
    /// <param name="bodyHtml">HTML body of the email.</param>
    Task SendAsync(string to, string subject, string bodyHtml, CancellationToken ct = default);
}
