using System.Net;
using System.Net.Http.Json;
using BannerShop.Api.Controllers;
using BannerShop.Api.Models.Analytics;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BannerShop.Tests.Controllers;

/// <summary>
/// Tests for visitor-traffic analytics (BANNERSH-285): the anonymous
/// <c>POST /api/analytics/track</c> ingest path, the <see cref="AnalyticsController.ClassifyReferrer"/>
/// bucketing rules, and the admin aggregation endpoints
/// (<c>/api/admin/analytics/traffic</c> and <c>/api/admin/analytics/referrers</c>).
///
/// None of this had coverage before — the referrer classifier in particular is
/// pure business logic that drives the admin dashboard's source breakdown.
/// </summary>
public class AnalyticsControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AnalyticsControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── ClassifyReferrer (pure static rules) ─────────────────────────────────

    [Theory]
    [InlineData(null, "Direct")]
    [InlineData("", "Direct")]
    [InlineData("   ", "Direct")]
    [InlineData("https://www.google.com/search?q=banner", "Google")]
    [InlineData("https://googleads.g.doubleclick.net/x", "Google")]
    [InlineData("https://www.facebook.com/", "Facebook")]
    [InlineData("https://fb.me/abc", "Facebook")]
    [InlineData("https://www.instagram.com/p/x", "Instagram")]
    [InlineData("https://l.instagram.com/?u=x", "Instagram")]
    [InlineData("https://t.co/abcd", "Twitter/X")]
    [InlineData("https://twitter.com/user", "Twitter/X")]
    [InlineData("https://x.com/user", "Twitter/X")]
    [InlineData("https://www.tiktok.com/@user", "TikTok")]
    [InlineData("https://www.bing.com/search", "Other")]
    [InlineData("https://some-blog.example/post", "Other")]
    // Regression: domains that merely END IN a network domain as a substring
    // (netflix.com / wix.com / phoenix.com all contain "x.com") must NOT be
    // swept into Twitter/X, and a network name in the query string (not the
    // host) must NOT be attributed to that network.
    [InlineData("https://www.netflix.com/browse", "Other")]
    [InlineData("https://www.wix.com/", "Other")]
    [InlineData("https://phoenix.com/", "Other")]
    [InlineData("https://mysite.com/?ref=google.com", "Other")]
    // ...but a real subdomain of a network domain still matches.
    [InlineData("https://mobile.x.com/user", "Twitter/X")]
    public void ClassifyReferrer_MapsToExpectedSource(string? referrer, string expected)
    {
        AnalyticsController.ClassifyReferrer(referrer).Should().Be(expected);
    }

    // ── POST /api/analytics/track ────────────────────────────────────────────

    [Fact]
    public async Task Track_ValidPayload_PersistsClassifiedPageView()
    {
        var client = _factory.CreateClient();
        var sessionId = Guid.NewGuid().ToString("N");

        var response = await client.PostAsJsonAsync("/api/analytics/track", new
        {
            sessionId,
            isNewSession = true,
            path = "/banner-builder/ai",
            referrer = "https://www.google.com/search?q=banner",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        PageView? saved = null;
        _factory.SeedDatabase(db =>
            saved = db.PageViews.AsNoTracking().FirstOrDefault(pv => pv.SessionId == sessionId));

        saved.Should().NotBeNull();
        saved!.Path.Should().Be("/banner-builder/ai");
        saved.IsNewSession.Should().BeTrue();
        saved.ReferrerSource.Should().Be("Google");
    }

    [Fact]
    public async Task Track_TruncatesOverlongFields()
    {
        var client = _factory.CreateClient();
        var sessionId = new string('s', 100); // > 36-char cap
        var longPath = "/" + new string('p', 600); // > 500-char cap

        var response = await client.PostAsJsonAsync("/api/analytics/track", new
        {
            sessionId,
            isNewSession = false,
            path = longPath,
            referrer = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var truncatedSessionId = sessionId.Substring(0, 36);
        PageView? saved = null;
        _factory.SeedDatabase(db =>
            saved = db.PageViews.AsNoTracking()
                .FirstOrDefault(pv => pv.SessionId == truncatedSessionId));

        saved.Should().NotBeNull();
        saved!.SessionId.Length.Should().Be(36);
        saved.Path.Length.Should().Be(500);
        saved.ReferrerSource.Should().Be("Direct");
    }

    [Theory]
    [InlineData("", "/")]
    [InlineData("sess", "")]
    public async Task Track_MissingRequiredField_Returns400(string sessionId, string path)
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/analytics/track", new
        {
            sessionId,
            isNewSession = true,
            path,
            referrer = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── GET /api/admin/analytics/traffic ─────────────────────────────────────

    [Fact]
    public async Task Traffic_AggregatesPageViewsAndSessionsIntoDailyBuckets()
    {
        // Isolate from other tests' rows via a unique historical window.
        var day1 = new DateTime(2001, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2001, 6, 2, 10, 0, 0, DateTimeKind.Utc);

        _factory.SeedDatabase(db =>
        {
            db.PageViews.AddRange(
                MakeView("traffic-s1", day1, isNew: true),
                MakeView("traffic-s1", day1.AddHours(1), isNew: false),
                MakeView("traffic-s1", day1.AddHours(2), isNew: false),
                MakeView("traffic-s2", day2, isNew: true),
                MakeView("traffic-s2", day2.AddHours(1), isNew: false));
            db.SaveChanges();
        });

        var client = _factory.CreateAuthenticatedClient(role: UserRole.Admin);
        var summary = await (await client.GetAsync(
            "/api/admin/analytics/traffic?fromUtc=2001-05-31T00:00:00Z&toUtc=2001-06-03T00:00:00Z&groupBy=day"))
            .ReadJsonAsync<TrafficSummary>();

        summary.Should().NotBeNull();
        summary!.TotalPageViews.Should().Be(5);
        summary.TotalSessions.Should().Be(2);
        summary.NewSessions.Should().Be(2);
        summary.Buckets.Should().HaveCount(2);

        var first = summary.Buckets[0];
        first.Label.Should().Be("2001-06-01");
        first.PageViews.Should().Be(3);
        first.UniqueSessions.Should().Be(1);
        first.NewSessions.Should().Be(1);
    }

    // ── GET /api/admin/analytics/referrers ───────────────────────────────────

    [Fact]
    public async Task Referrers_GroupsBySourceOrderedBySessionsDescending()
    {
        var when = new DateTime(2002, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        _factory.SeedDatabase(db =>
        {
            db.PageViews.AddRange(
                MakeView("ref-g1", when, source: "Google"),
                MakeView("ref-g1", when.AddHours(1), source: "Google"),
                MakeView("ref-g2", when.AddHours(2), source: "Google"),
                MakeView("ref-d1", when.AddHours(3), source: "Direct"));
            db.SaveChanges();
        });

        var client = _factory.CreateAuthenticatedClient(role: UserRole.Admin);
        var stats = await (await client.GetAsync(
            "/api/admin/analytics/referrers?fromUtc=2002-05-31T00:00:00Z&toUtc=2002-06-02T00:00:00Z"))
            .ReadJsonAsync<ReferrerStats>();

        stats.Should().NotBeNull();
        var google = stats!.Sources.Single(s => s.Source == "Google");
        google.Sessions.Should().Be(2);
        google.PageViews.Should().Be(3);

        var direct = stats.Sources.Single(s => s.Source == "Direct");
        direct.Sessions.Should().Be(1);
        direct.PageViews.Should().Be(1);

        // Ordered by session count descending → Google (2) before Direct (1).
        stats.Sources[0].Source.Should().Be("Google");
    }

    [Fact]
    public async Task AdminAnalytics_WithoutAdminRole_Returns403()
    {
        var client = _factory.CreateAuthenticatedClient(role: UserRole.Customer);

        var response = await client.GetAsync("/api/admin/analytics/traffic");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static PageView MakeView(string sessionId, DateTime createdAt,
        bool isNew = false, string source = "Direct") => new()
    {
        SessionId = sessionId,
        IsNewSession = isNew,
        Path = "/",
        ReferrerSource = source,
        CreatedAt = createdAt,
    };
}
