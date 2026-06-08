using System.Security.Claims;
using BannerShop.Api.Models.DesignRequests;
using BannerShop.Api.Services.AiCredits;
using BannerShop.Api.Services.DesignRequests;
using BannerShop.Core.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BannerShop.Api.Controllers;

/// <summary>
/// Endpoints for the AI banner builder (free-first, credit-gated — BANNERSH-67) and the
/// human-designer flow (495 kr). The POST /ai endpoint accepts both anonymous and
/// authenticated callers; everything else requires auth.
/// </summary>
[ApiController]
[Route("api/design-requests")]
[Authorize]
public class DesignRequestsController : ControllerBase
{
    private readonly IDesignRequestService _service;

    public DesignRequestsController(IDesignRequestService service)
    {
        _service = service;
    }

    // ── POST /api/design-requests/manual ────────────────────────────────────
    [HttpPost("manual")]
    public async Task<IActionResult> CreateManual([FromBody] CreateManualDesignRequestDto req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();

        var result = await _service.CreateManualRequestAsync(userId, req, ct);
        if (!result.Success) return BadRequest(new { error = result.Error });

        return Ok(new CreateDesignRequestResponseDto
        {
            DesignRequestId = result.DesignRequestId,
            ClientSecret = result.ClientSecret ?? string.Empty,
            TotalNok = result.TotalNok
        });
    }

    // ── POST /api/design-requests/{id}/revision ──────────────────────────────
    [HttpPost("{id:int}/revision")]
    public async Task<IActionResult> RequestRevision(int id, [FromBody] RequestRevisionDto req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();

        var result = await _service.RequestRevisionAsync(id, userId, req.Comment, ct);
        if (!result.Success) return BadRequest(new { error = result.Error });
        return Ok(result.Detail);
    }

    // ── POST /api/design-requests/ai ─────────────────────────────────────────
    /// <summary>
    /// Creates an AI design request under the BANNERSH-67 free-first flow.
    ///
    /// Anonymous callers (no Bearer token) get one free generation per IP per rolling
    /// 30 days. Authenticated callers get one free generation per account, then must
    /// have purchased / been-granted credits to generate more. No upfront payment.
    /// </summary>
    [HttpPost("ai")]
    [AllowAnonymous]
    [ServiceFilter(typeof(BotProtectionFilter))]
    public async Task<IActionResult> CreateAi([FromBody] CreateAiDesignRequestDto req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var rawUserId = GetUserId();
        int? userId = rawUserId == 0 ? null : rawUserId;

        // Only resolve the IP for anonymous callers — auth callers are throttled by
        // their per-user credit balance, not by IP.
        var ipAddress = userId is null ? GetClientIpAddress() : null;

        var result = await _service.CreateAiRequestAsync(userId, ipAddress, req, ct);

        return result.StatusCode switch
        {
            201 => StatusCode(201, new CreateAiDesignRequestResponseDto
            {
                DesignRequestId = result.DesignRequestId,
                RequiresAuth = result.RequiresAuth,
                CreditsRemaining = result.CreditsRemaining
            }),
            402 => StatusCode(402, result.Paywall),
            404 => NotFound(new { error = result.Error }),
            _ => BadRequest(new { error = result.Error })
        };
    }

    private string? GetClientIpAddress()
    {
        // Prefer the standard X-Forwarded-For (first hop) when behind a proxy.
        var forwarded = Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var first = forwarded.Split(',')[0].Trim();
            if (!string.IsNullOrEmpty(first)) return first;
        }
        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }

    // ── GET /api/design-requests ─────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> ListMine(CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        var rows = await _service.ListMineAsync(userId, ct);
        return Ok(rows);
    }

    // ── GET /api/design-requests/{id} ────────────────────────────────────────
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        var isAdmin = User.IsInRole(nameof(UserRole.Admin));
        var dto = await _service.GetAsync(id, userId, isAdmin, ct);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    // ── POST /api/design-requests/{id}/approve ───────────────────────────────
    [HttpPost("{id:int}/approve")]
    public async Task<IActionResult> Approve(int id, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        var result = await _service.ApproveAsync(id, userId, ct);
        if (!result.Success) return BadRequest(new { error = result.Error });
        return Ok(result.Detail);
    }

    // ── POST /api/design-requests/{id}/regenerate ────────────────────────────
    /// <summary>
    /// Consumes 1 AI credit and enqueues a new generation attempt with optionally updated inputs.
    /// Returns 202 on success, 402 when the user has no credits (with paywall metadata).
    /// </summary>
    [HttpPost("{id:int}/regenerate")]
    public async Task<IActionResult> Regenerate(int id, [FromBody] RegenerateAiRequestDto? req, CancellationToken ct)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();

        var result = await _service.RegenerateAsync(id, userId, req ?? new RegenerateAiRequestDto(), ct);

        return result.StatusCode switch
        {
            202 => Accepted(new RegenerateAiResponseDto
            {
                GenerationId = result.GenerationId,
                CreditsRemaining = result.CreditsRemaining
            }),
            402 => StatusCode(402, new
            {
                error = result.Error,
                creditsRemaining = result.CreditsRemaining,
                paywallMetadata = result.PaywallMetadata
            }),
            403 => Forbid(),
            404 => NotFound(new { error = result.Error }),
            _ => BadRequest(new { error = result.Error })
        };
    }

    private int GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : 0;
    }
}
