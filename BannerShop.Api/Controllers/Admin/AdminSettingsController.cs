using BannerShop.Api.Services.SystemSettings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BannerShop.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/settings")]
[Authorize(Roles = "Admin")]
public class AdminSettingsController : ControllerBase
{
    private readonly ISystemSettingsService _settings;

    public AdminSettingsController(ISystemSettingsService settings) => _settings = settings;

    // ── GET /api/admin/settings ───────────────────────────────────────────────
    /// <summary>
    /// Returns all system settings. Sensitive values (e.g. API keys) are
    /// masked in the response so they don't leak into browser history/logs,
    /// but a non-empty masked value lets the UI show "key is set".
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var all = await _settings.GetAllAsync(ct);
        var response = all.Select(s => new
        {
            s.Id,
            s.Key,
            s.Label,
            s.IsSensitive,
            // Return masked value for sensitive settings so the UI can tell
            // whether a key has been configured without exposing the actual key.
            Value = s.IsSensitive && !string.IsNullOrEmpty(s.Value)
                ? "••••••••"
                : s.Value
        });
        return Ok(response);
    }

    // ── PUT /api/admin/settings/{key} ────────────────────────────────────────
    [HttpPut("{key}")]
    public async Task<IActionResult> Update(string key, [FromBody] UpdateSettingRequest req, CancellationToken ct)
    {
        if (req.Value is null)
            return BadRequest(new { error = "Value is required." });

        await _settings.SetValueAsync(key, req.Value.Trim(), ct);

        // Return the updated (masked) view.
        var all = await _settings.GetAllAsync(ct);
        var updated = all.FirstOrDefault(s => s.Key == key);
        if (updated is null)
            return NotFound();

        return Ok(new
        {
            updated.Id,
            updated.Key,
            updated.Label,
            updated.IsSensitive,
            Value = updated.IsSensitive && !string.IsNullOrEmpty(updated.Value)
                ? "••••••••"
                : updated.Value
        });
    }
}

public sealed record UpdateSettingRequest(string? Value);
