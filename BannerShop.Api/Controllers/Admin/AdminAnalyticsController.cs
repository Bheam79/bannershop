using BannerShop.Api.Models.Analytics;
using BannerShop.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Controllers.Admin;

/// <summary>
/// Admin endpoints for visitor traffic analytics (BANNERSH-285).
/// All routes require Admin role.
/// </summary>
[ApiController]
[Route("api/admin/analytics")]
[Authorize(Roles = "Admin")]
public class AdminAnalyticsController : ControllerBase
{
    private readonly BannerShopDbContext _db;

    public AdminAnalyticsController(BannerShopDbContext db)
    {
        _db = db;
    }

    // ── GET /api/admin/analytics/traffic ────────────────────────────────────
    /// <summary>
    /// Returns a time-bucketed breakdown of page views, unique sessions and new
    /// sessions for the requested period.
    /// groupBy: "hour" | "day" (default "day")
    /// Default range: last 30 days.
    /// </summary>
    [HttpGet("traffic")]
    public async Task<ActionResult<TrafficSummary>> Traffic(
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] string groupBy = "day",
        CancellationToken ct = default)
    {
        var to   = (toUtc   ?? DateTime.UtcNow).ToUniversalTime();
        var from = (fromUtc ?? to.AddDays(-30)).ToUniversalTime();
        if (from > to) (from, to) = (to, from);

        var rows = await _db.PageViews
            .AsNoTracking()
            .Where(pv => pv.CreatedAt >= from && pv.CreatedAt <= to)
            .Select(pv => new { pv.CreatedAt, pv.SessionId, pv.IsNewSession })
            .ToListAsync(ct);

        // Build buckets
        var byBucket = groupBy.ToLowerInvariant() == "hour"
            ? rows.GroupBy(r => new DateTime(r.CreatedAt.Year, r.CreatedAt.Month,
                                              r.CreatedAt.Day, r.CreatedAt.Hour, 0, 0, DateTimeKind.Utc))
            : rows.GroupBy(r => new DateTime(r.CreatedAt.Year, r.CreatedAt.Month,
                                              r.CreatedAt.Day, 0, 0, 0, DateTimeKind.Utc));

        var buckets = byBucket
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var label = groupBy.ToLowerInvariant() == "hour"
                    ? g.Key.ToString("yyyy-MM-dd HH:mm")
                    : g.Key.ToString("yyyy-MM-dd");
                return new SessionBucket(
                    label,
                    g.Key,
                    g.Count(),
                    g.Select(r => r.SessionId).Distinct().Count(),
                    g.Count(r => r.IsNewSession)
                );
            })
            .ToList();

        var allSessions = rows.Select(r => r.SessionId).Distinct().Count();

        return Ok(new TrafficSummary(
            rows.Count,
            allSessions,
            rows.Count(r => r.IsNewSession),
            buckets
        ));
    }

    // ── GET /api/admin/analytics/referrers ───────────────────────────────────
    /// <summary>
    /// Returns session and page-view counts grouped by referrer source
    /// (Direct, Google, Facebook, Instagram, Twitter/X, TikTok, Other).
    /// </summary>
    [HttpGet("referrers")]
    public async Task<ActionResult<ReferrerStats>> Referrers(
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        CancellationToken ct = default)
    {
        var to   = (toUtc   ?? DateTime.UtcNow).ToUniversalTime();
        var from = (fromUtc ?? to.AddDays(-30)).ToUniversalTime();
        if (from > to) (from, to) = (to, from);

        var rows = await _db.PageViews
            .AsNoTracking()
            .Where(pv => pv.CreatedAt >= from && pv.CreatedAt <= to)
            .Select(pv => new { pv.ReferrerSource, pv.SessionId })
            .ToListAsync(ct);

        var sources = rows
            .GroupBy(r => r.ReferrerSource)
            .Select(g => new ReferrerStat(
                g.Key,
                g.Select(r => r.SessionId).Distinct().Count(),
                g.Count()
            ))
            .OrderByDescending(s => s.Sessions)
            .ToList();

        return Ok(new ReferrerStats(sources));
    }
}
