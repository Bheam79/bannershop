using BannerShop.Api.Services.Orders.Stripe;
using BannerShop.Api.Services.SystemSettings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

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
    private readonly StripeOptions _stripeOptions;

    public ConfigController(ISystemSettingsService settings, IOptions<StripeOptions> stripeOptions)
    {
        _settings = settings;
        _stripeOptions = stripeOptions.Value;
    }

    // ── GET /api/config/stripe ───────────────────────────────────────────────
    /// <summary>
    /// Returns the Stripe publishable key so the frontend can load Stripe.js at
    /// runtime without baking the key into the build. DB setting wins over appsettings.
    /// Returns an empty string if neither is set (payment UI will show "not configured").
    /// </summary>
    [HttpGet("stripe")]
    public async Task<IActionResult> GetStripeConfig(CancellationToken ct)
    {
        // DB setting (admin panel) wins over appsettings.
        var dbPk = await _settings.GetValueAsync("stripe_publishable_key", ct);
        var publishableKey = !string.IsNullOrWhiteSpace(dbPk) ? dbPk : _stripeOptions.PublishableKey;

        return Ok(new { publishableKey = publishableKey ?? string.Empty });
    }
}
