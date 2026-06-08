using System.Security.Claims;
using BannerShop.Api.Services.AiCredits;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BannerShop.Api.Controllers;

/// <summary>
/// AI generation credit pool endpoints (BANNERSH-65).
/// </summary>
[ApiController]
[Route("api/ai-credits")]
public class AiCreditsController : ControllerBase
{
    private readonly IAiCreditService _credits;

    public AiCreditsController(IAiCreditService credits)
    {
        _credits = credits;
    }

    // ── GET /api/ai-credits/me ───────────────────────────────────────────────
    /// <summary>Returns the current credit balance and free-generation status for the caller.</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();

        var balance = await _credits.GetBalanceWithDetailsAsync(userId, ct);
        return Ok(new
        {
            creditsRemaining = balance.CreditsRemaining,
            hasUsedFreeGeneration = balance.HasUsedFreeGeneration
        });
    }

    private int GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : 0;
    }
}
