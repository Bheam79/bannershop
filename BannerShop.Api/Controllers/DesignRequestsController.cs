using System.Security.Claims;
using BannerShop.Api.Models.DesignRequests;
using BannerShop.Api.Services.DesignRequests;
using BannerShop.Core.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BannerShop.Api.Controllers;

/// <summary>
/// Endpoints for the AI banner builder (95 kr) and (later) the manual flow (495 kr).
/// Implements the AI surface from BANNERSH-19; the manual endpoints will be added by
/// BANNERSH-21 and can reuse this controller.
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
    [HttpPost("ai")]
    public async Task<IActionResult> CreateAi([FromBody] CreateAiDesignRequestDto req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();

        var result = await _service.CreateAiRequestAsync(userId, req, ct);
        if (!result.Success) return BadRequest(new { error = result.Error });

        return Ok(new CreateDesignRequestResponseDto
        {
            DesignRequestId = result.DesignRequestId,
            ClientSecret = result.ClientSecret ?? string.Empty,
            TotalNok = result.TotalNok
        });
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

    private int GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : 0;
    }
}
