using System.Security.Claims;
using BannerShop.Api.Models.Analytics;
using BannerShop.Core.Entities;
using BannerShop.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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
    // Anonymous + unauthenticated, so rate-limit per IP to stop a scripted
    // flood from writing unbounded PageView rows (same pattern as auth-*).
    [HttpPost("track")]
    [EnableRateLimiting("analytics-track")]
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

        // Classify on the referrer's HOST, not the raw URL, so that (a) a path or
        // query fragment that merely contains a network's name (e.g.
        // "https://mysite.com/?ref=google.com") isn't misattributed, and (b) an
        // unrelated domain that ends in a network's domain as a substring
        // (e.g. "netflix.com" / "wix.com" both contain "x.com") isn't swept into
        // Twitter/X. For host-specific domains we require an exact host or a
        // "sub.domain" boundary via HostIs; brand names that vary by TLD/subdomain
        // (google.*, facebook.*, instagram.*, tiktok.*) still match by substring.
        string host;
        if (Uri.TryCreate(referrer, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
            host = uri.Host.ToLowerInvariant();
        else
            host = referrer.ToLowerInvariant(); // best-effort for a non-absolute referrer

        // True when host == domain or is a subdomain of it (host ends in ".domain").
        static bool HostIs(string host, string domain) =>
            host == domain || host.EndsWith("." + domain, StringComparison.Ordinal);

        if (host.Contains("google") || host.Contains("gads"))
            return "Google";

        if (host.Contains("facebook") || HostIs(host, "fb.com") || HostIs(host, "fb.me"))
            return "Facebook";

        if (host.Contains("instagram"))
            return "Instagram";

        if (HostIs(host, "t.co") || host.Contains("twitter") || HostIs(host, "x.com"))
            return "Twitter/X";

        if (host.Contains("tiktok"))
            return "TikTok";

        return "Other";
    }
}
