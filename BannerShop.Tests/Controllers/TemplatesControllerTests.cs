using System.Net;
using System.Text.Json;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests.Controllers;

/// <summary>
/// Integration tests for TemplatesController (GET /api/templates).
/// </summary>
public class TemplatesControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public TemplatesControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        EnsureTemplatesSeeded();
    }

    private void EnsureTemplatesSeeded()
    {
        _factory.SeedDatabase(db =>
        {
            if (!db.BannerTemplates.Any())
            {
                db.BannerTemplates.AddRange(
                    new BannerTemplate
                    {
                        Id = 101, Category = BannerTemplateCategory.Birthday,
                        NameNb = "Bursdag", NameEn = "Birthday", SortOrder = 10
                    },
                    new BannerTemplate
                    {
                        Id = 102, Category = BannerTemplateCategory.Wedding,
                        NameNb = "Bryllup", NameEn = "Wedding", SortOrder = 20
                    }
                );
                db.SaveChanges();
            }
        });
    }

    [Fact]
    public async Task GetTemplates_Returns200WithList()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/templates");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var items = JsonSerializer.Deserialize<JsonElement[]>(body, _json)!;
        items.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetTemplates_NoAuthRequired_Returns200()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/templates");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetTemplates_ResponseContainsExpectedFields()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/templates");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("nameNb");
        body.Should().Contain("nameEn");
        body.Should().Contain("category");
        body.Should().Contain("sortOrder");
    }

    [Fact]
    public async Task GetTemplates_ContainsSeededTemplate()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/templates");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Bursdag");
    }
}
