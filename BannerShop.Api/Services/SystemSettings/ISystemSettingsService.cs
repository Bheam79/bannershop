namespace BannerShop.Api.Services.SystemSettings;

/// <summary>
/// Provides access to admin-editable runtime settings stored in the database.
/// Settings take precedence over appsettings.json values so the admin can
/// update them via the UI without redeploying or restarting the service.
/// </summary>
public interface ISystemSettingsService
{
    /// <summary>
    /// Returns the value for <paramref name="key"/>, or <c>null</c> if the key
    /// does not exist or its value is empty.
    /// </summary>
    Task<string?> GetValueAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Persists a new value for <paramref name="key"/>. Creates the row if it
    /// does not already exist.
    /// </summary>
    Task SetValueAsync(string key, string value, CancellationToken ct = default);

    /// <summary>Returns all settings (key, label, isSensitive, current value).</summary>
    Task<IReadOnlyList<SystemSettingDto>> GetAllAsync(CancellationToken ct = default);
}

public sealed record SystemSettingDto(
    int Id,
    string Key,
    string Label,
    bool IsSensitive,
    string Value);
