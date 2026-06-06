namespace BannerShop.Api.Services.Email;

/// <summary>
/// SMTP configuration for optional email notifications.
/// All fields are optional — when SmtpHost is empty the NullEmailService is used.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Sender address shown in the From header.</summary>
    public string From { get; set; } = string.Empty;

    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public string SmtpUser { get; set; } = string.Empty;
    public string SmtpPass { get; set; } = string.Empty;
}
