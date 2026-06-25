using System.Security.Claims;
using BannerShop.Api.Models.Analytics;
using BannerShop.Core.Entities;
using BannerShop.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BannerShop.Api.Controllers;

/// <summary>
/// Public endpoint that the frontend calls on every route change to record
/// page-view traffic.  AllowAnonymous so pre-login visitors are tracked too.
/// </summary>
[ApiController]
[Route("api/analytics")]
[AllowAnonymous]
public class AnalyticsController : ControllerBase
{
    private readonly BannerShopDbContext _db;
    private readonly ILogger<AnalyticsController> _log;

    public AnalyticsController(BannerShopDbContext db, ILogger<AnalyticsController> log)
    {
        _db = db;
        _log = log;
    }

    // ── POST /api/analytics/track ────────────────────────────────────────────
    [HttpPost("track")]
    public async Task<IActionResult> Track([FromBody] TrackPageViewRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.SessionId) || string.IsNullOrWhiteSpace(req.Path))
            return BadRequest();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        int? userId = int.TryParse(userIdClaim, out var uid) ? uid : null;

        _db.PageViews.Add(new PageView
        {
            SessionId = req.SessionId[..Math.Min(36, req.SessionId.Length)],
            IsNewSession = req.IsNewSession,
            Path = req.Path[..Math.Min(500, req.Path.Length)],
            Referrer = req.Referrer != null
                ? req.Referrer[..Math.Min(1000, req.Referrer.Length)]
                : null,
            ReferrerSource = ClassifyReferrer(req.Referrer),
            IpAddress = ip,
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
        });

        await _db.SaveChangesAsync(ct);
        return Ok();
    }

    // ─────────────────────────────────────────────────────────────────────────
    internal static string ClassifyReferrer(string? referrer)
    {
        if (string.IsNullOrWhiteSpace(referrer)) return "Direct";

        var r = referrer.ToLowerInvariant();

        if (r.Contains("google.") || r.Contains("googleads") || r.Contains("gads"))
            return "Google";

        if (r.Contains("facebook.") || r.Contains("fb.com") || r.Contains("fb.me"))
            return "Facebook";

        if (r.Contains("instagram.") || r.Contains("l.instagram"))
            return "Instagram";

        if (r.Contains("t.co/") || r.Contains("twitter.") || r.Contains("x.com"))
            return "Twitter/X";

        if (r.Contains("tiktok."))
            return "TikTok";

        return "Other";
    }
}
