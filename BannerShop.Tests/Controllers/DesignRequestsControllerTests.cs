using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BannerShop.Api.Models.DesignRequests;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests.Controllers;

/// <summary>
/// Integration tests for POST /api/design-requests/ai (BANNERSH-67 free-first flow).
///
/// Covers the wiring between the controller, the BotProtectionFilter, the
/// AllowAnonymous policy, and the CreateAiRequestAsync service path. The service
/// internals themselves are exercised separately in <c>DesignRequestServiceTests</c>.
/// </summary>
public class DesignRequestsControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public DesignRequestsControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        EnsureSeed();
    }

    private void EnsureSeed()
    {
        _factory.SeedDatabase(db =>
        {
            if (!db.BannerTemplates.Any())
            {
                db.BannerTemplates.Add(new BannerTemplate
                {
                    Id = 1, Category = BannerTemplateCategory.Birthday,
                    NameNb = "Bursdag", NameEn = "Birthday", SortOrder = 10
                });
                db.PricingParameters.AddRange(
                    new PricingParameter { Id = 1011, Name = "AI credit pack price",          Key = "ai_credit_pack_price_nok",         Value = 29m  },
                    new PricingParameter { Id = 1012, Name = "AI credit pack count",          Key = "ai_credit_pack_count",             Value = 10m  },
                    new PricingParameter { Id = 1013, Name = "AI banner activation fee",      Key = "ai_banner_activation_fee_nok",     Value = 95m  });
                db.SaveChanges();
            }
        });
    }

    private static object SampleBody() => new
    {
        templateId = 1,
        language = "nb",
        personName = "Ola",
        textContent = "Gratulerer",
        themeDescription = "tropisk",
        aspectRatio = "16:9"
    };

    /// <summary>Returns a fresh anonymous client with a browser-like UA + integrity header.</summary>
    private HttpClient NewAnonClient()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36");
        c.DefaultRequestHeaders.Add("X-Request-Integrity", "abc123");
        return c;
    }

    // ── 403 on bot UA ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAi_anonymous_with_bot_useragent_returns_403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("python-requests/2.31");
        client.DefaultRequestHeaders.Add("X-Request-Integrity", "abc123");

        var response = await client.PostAsJsonAsync("/api/design-requests/ai", SampleBody());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateAi_anonymous_missing_integrity_header_returns_403()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36");
        // No X-Request-Integrity header.

        var response = await client.PostAsJsonAsync("/api/design-requests/ai", SampleBody());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
