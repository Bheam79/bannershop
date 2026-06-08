namespace BannerShop.Core.Entities;

/// <summary>
/// Simple key-value store for runtime-editable application settings such as
/// the OpenAI API key. Stored in the database so the admin can update them
/// via the admin panel without redeploying or restarting the service.
/// </summary>
public class SystemSetting
{
    public int Id { get; set; }

    /// <summary>Unique string key, e.g. "openai_api_key".</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>String value. Sensitive values (API keys) are stored in plain-text
    /// (same threat model as appsettings.json on the same host).</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Human-readable label shown in the admin settings page.</summary>
    public string? Label { get; set; }

    /// <summary>Whether this setting contains a sensitive value (e.g. a secret key).
    /// Admin UI will render it as a password field and the GET endpoint will mask it
    /// in the response.</summary>
    public bool IsSensitive { get; set; }
}
