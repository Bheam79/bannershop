using BannerShop.Api.Services.SystemSettings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BannerShop.Api.Controllers;

/// <summary>
/// Returns public runtime configuration the frontend needs but that may change
/// without a redeploy (e.g. Stripe publishable key set via the admin panel).
/// All endpoints here are public (no [Authorize]) because the data is non-sensitive.
/// </summary>
[ApiController]
[Route("api/config")]
[AllowAnonymous]
public class ConfigController : ControllerBase
{
    private readonly ISystemSettingsService _settings;

    public ConfigController(ISystemSettingsService settings)
    {
        _settings = settings;
    }

    // ── GET /api/config/stripe ───────────────────────────────────────────────
    /// <summary>
    /// Returns the Stripe publishable key so the frontend can load Stripe.js at
    /// runtime without baking the key into the build.
    ///
    /// BANNERSH-161: read EXCLUSIVELY from the database
    /// (system_settings.stripe_publishable_key) — no appsettings fallback.
    /// Returns an empty string when the row is not yet configured; the payment
    /// UI then shows "not configured" instead of attempting to load Stripe.js.
    /// </summary>
    [HttpGet("stripe")]
    public async Task<IActionResult> GetStripeConfig(CancellationToken ct)
    {
        var dbPk = await _settings.GetValueAsync("stripe_publishable_key", ct);
        return Ok(new { publishableKey = dbPk ?? string.Empty });
    }
}
