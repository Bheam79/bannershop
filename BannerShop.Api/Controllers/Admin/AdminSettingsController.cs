using BannerShop.Api.Services.SystemSettings;
using BannerShop.Api.Services.DesignRequests;
using Microsoft.Extensions.Options;
using BannerShop.Api.Services.DesignRequests.Claude;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BannerShop.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/settings")]
[Authorize(Roles = "Admin")]
public class AdminSettingsController : ControllerBase
{
    private readonly ISystemSettingsService _settings;
    private readonly ClaudeOAuthTokenManager _claudeOAuth;

    public AdminSettingsController(
        ISystemSettingsService settings,
        ClaudeOAuthTokenManager claudeOAuth)
    {
        _settings = settings;
        _claudeOAuth = claudeOAuth;
    }

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

    /// <summary>App-owned prompt defaults, active overrides and configured model selections only.</summary>
    [HttpGet("prompts")]
    public async Task<IActionResult> GetPrompts(
        [FromServices] IOptionsMonitor<ClaudeCliOptions> claudeOptions, CancellationToken ct) =>
        Ok(await BannerPromptCatalog.BuildAsync(_settings, claudeOptions.CurrentValue, ct));

    // ── PUT /api/admin/settings/{key} ────────────────────────────────────────
    [HttpPut("{key}")]
    public async Task<IActionResult> Update(string key, [FromBody] UpdateSettingRequest req, CancellationToken ct)
    {
        if (key is "codex_image_cli_credentials" or "grok_image_cli_credentials")
            return BadRequest(new { error = "Use the image provider OAuth connection controls." });

        if (req.Value is null)
            return BadRequest(new { error = "Value is required." });

        if (string.Equals(key, ClaudeOAuthTokenManager.AccessTokenSetting, StringComparison.Ordinal))
        {
            // A manually entered setup-token replaces (rather than mixes with)
            // any refreshable OAuth credential from a previous browser flow.
            await _settings.SetValuesAsync(new Dictionary<string, string>
            {
                [ClaudeOAuthTokenManager.AccessTokenSetting] = req.Value.Trim(),
                [ClaudeOAuthTokenManager.RefreshTokenSetting] = "",
                [ClaudeOAuthTokenManager.ExpiresAtSetting] = ""
            }, ct);
        }
        else
        {
            await _settings.SetValueAsync(key, req.Value.Trim(), ct);
        }

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

    /// <summary>Creates a ten-minute Claude Code OAuth PKCE authorization URL.</summary>
    [HttpPost("claude-oauth/start")]
    public IActionResult StartClaudeOAuth() => Ok(_claudeOAuth.StartAuthorization());

    /// <summary>
    /// Exchanges the code#state value displayed by Claude after authorization.
    /// Access and refresh tokens are persisted as masked sensitive settings.
    /// </summary>
    [HttpPost("claude-oauth/complete")]
    public async Task<IActionResult> CompleteClaudeOAuth(
        [FromBody] CompleteClaudeOAuthRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(new { error = "Code is required." });

        try
        {
            await _claudeOAuth.CompleteAuthorizationAsync(req.Code, ct);
            return Ok(await _claudeOAuth.GetStatusAsync(ct));
        }
        catch (ClaudeOAuthInputException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (ClaudeOAuthProtocolException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
        }
    }

    /// <summary>Returns credential metadata only; token values are never exposed.</summary>
    [HttpGet("claude-oauth/status")]
    public async Task<IActionResult> GetClaudeOAuthStatus(CancellationToken ct) =>
        Ok(await _claudeOAuth.GetStatusAsync(ct));
}

public sealed record UpdateSettingRequest(string? Value);
public sealed record CompleteClaudeOAuthRequest(string? Code);
