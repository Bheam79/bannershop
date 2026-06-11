using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Moq;
using Xunit;

namespace BannerShop.Tests.Controllers;

/// <summary>
/// Integration tests for BannerBuilderController.
/// Uses BannerBuilderTestFactory which mocks IImageProcessingService.
/// Tests focus on the DB-backed endpoints that don't write real files.
/// </summary>
public class BannerBuilderControllerTests : IClassFixture<BannerBuilderTestFactory>
{
    private readonly BannerBuilderTestFactory _factory;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public BannerBuilderControllerTests(BannerBuilderTestFactory factory)
    {
        _factory = factory;
        EnsureDesignsSeeded();
    }

    private void EnsureDesignsSeeded()
    {
        _factory.SeedDatabase(db =>
        {
            if (!db.BannerDesigns.Any(d => d.Id == 301))
            {
                // Seed a user
                if (!db.Users.Any(u => u.Id == 700))
                {
                    db.Users.Add(new User
                    {
                        Id = 700, Email = "builder@test.com", Name = "Builder User",
                        PasswordHash = "x", Role = UserRole.Customer, CreatedAt = DateTime.UtcNow
                    });
                }
                db.BannerDesigns.AddRange(
                    new BannerDesign
                    {
                        Id = 301, UserId = 700,
                        OriginalFileName = "test-image.jpg",
                        StoragePath = "banner-builder/700/test.jpg",
                        ContentType = "image/jpeg",
                        WidthPx = 1920, HeightPx = 1080,
                        RotationDegrees = 0,
                        SelectedHeightCm = 150,
                        ComputedWidthCm = 267,
                        PreviewStoragePath = null,   // no preview file — tests don't use real files
                        CreatedAt = DateTime.UtcNow
                    },
                    new BannerDesign
                    {
                        Id = 302, UserId = 700,
                        OriginalFileName = "another.png",
                        StoragePath = "banner-builder/700/another.png",
                        ContentType = "image/png",
                        WidthPx = 3000, HeightPx = 2000,
                        RotationDegrees = 0,
                        SelectedHeightCm = 100,
                        ComputedWidthCm = 150,
                        PreviewStoragePath = null,
                        CreatedAt = DateTime.UtcNow
                    },
                    // Design owned by a different user
                    new BannerDesign
                    {
                        Id = 303, UserId = 999,
                        OriginalFileName = "other.jpg",
                        StoragePath = "banner-builder/999/other.jpg",
                        ContentType = "image/jpeg",
                        WidthPx = 800, HeightPx = 600,
                        RotationDegrees = 0,
                        SelectedHeightCm = 150,
                        ComputedWidthCm = 200,
                        PreviewStoragePath = null,
                        CreatedAt = DateTime.UtcNow
                    },
                    // Design with a PreviewStoragePath — used for rotate-with-preview test
                    new BannerDesign
                    {
                        Id = 305, UserId = 700,
                        OriginalFileName = "with-preview.jpg",
                        StoragePath = "banner-builder/700/with-preview.jpg",
                        ContentType = "image/jpeg",
                        WidthPx = 1920, HeightPx = 1080,
                        RotationDegrees = 0,
                        SelectedHeightCm = 150,
                        ComputedWidthCm = 267,
                        PreviewStoragePath = "banner-builder/700/with-preview-thumb.jpg",
                        CreatedAt = DateTime.UtcNow
                    }
                );
                db.SaveChanges();
            }
        });
    }

    private HttpClient UserClient(int userId = 700) =>
        _factory.CreateAuthenticatedClient(userId: userId, email: "builder@test.com", name: "Builder User");

    private HttpClient AdminClient() =>
        _factory.CreateAuthenticatedClient(userId: 99, email: "admin@test.com", name: "Admin", role: UserRole.Admin);

    // ── GET /api/banner-builder/{id} ──────────────────────────────────────────

    [Fact]
    public async Task GetDesign_OwnDesign_Returns200()
    {
        var response = await UserClient().GetAsync("/api/banner-builder/301");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("widthPx");
        body.Should().Contain("heightPx");
        body.Should().Contain("rotationDegrees");
    }

    [Fact]
    public async Task GetDesign_NonExistentId_Returns404()
    {
        var response = await UserClient().GetAsync("/api/banner-builder/99999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetDesign_OtherUsersDesign_Returns403()
    {
        var response = await UserClient(700).GetAsync("/api/banner-builder/303");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetDesign_AnonymousClient_CanAccessOwnDesign()
    {
        // Anonymous designs (UserId=null) are accessible by anyone
        _factory.SeedDatabase(db =>
        {
            if (!db.BannerDesigns.Any(d => d.Id == 304))
            {
                db.BannerDesigns.Add(new BannerDesign
                {
                    Id = 304, UserId = null,  // anonymous
                    OriginalFileName = "anon.jpg",
                    StoragePath = "banner-builder/0/anon.jpg",
                    ContentType = "image/jpeg",
                    WidthPx = 800, HeightPx = 600,
                    RotationDegrees = 0,
                    SelectedHeightCm = 150,
                    ComputedWidthCm = 200,
                    CreatedAt = DateTime.UtcNow
                });
                db.SaveChanges();
            }
        });

        var response = await _factory.CreateClient().GetAsync("/api/banner-builder/304");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetDesign_AdminCanAccessAnyDesign_Returns200()
    {
        var response = await AdminClient().GetAsync("/api/banner-builder/303");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── GET /api/banner-builder/mine ──────────────────────────────────────────

    [Fact]
    public async Task ListMine_AuthenticatedUser_Returns200WithOwnDesigns()
    {
        var response = await UserClient().GetAsync("/api/banner-builder/mine");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var items = JsonSerializer.Deserialize<JsonElement[]>(body, _json)!;
        // User 700 has designs 301 and 302
        items.Length.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task ListMine_Anonymous_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/banner-builder/mine");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── PUT /api/banner-builder/{id}/height ───────────────────────────────────

    [Fact]
    public async Task SetHeight_ValidRequest_Returns200WithUpdatedDimensions()
    {
        var response = await UserClient().PutAsJsonAsync("/api/banner-builder/301/height",
            new { heightCm = 200 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonSerializer.Deserialize<JsonElement>(body, _json);
        doc.GetProperty("selectedHeightCm").GetInt32().Should().Be(200);
    }

    [Fact]
    public async Task SetHeight_NonExistentDesign_Returns404()
    {
        var response = await UserClient().PutAsJsonAsync("/api/banner-builder/99999/height",
            new { heightCm = 150 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SetHeight_OtherUsersDesign_Returns403()
    {
        var response = await UserClient(700).PutAsJsonAsync("/api/banner-builder/303/height",
            new { heightCm = 150 });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── PUT /api/banner-builder/{id}/rotate ───────────────────────────────────

    [Fact]
    public async Task Rotate_ValidRequest_NoPreview_Returns200()
    {
        // Design 302 has no PreviewStoragePath so image regen is skipped
        var response = await UserClient().PutAsJsonAsync("/api/banner-builder/302/rotate",
            new { degrees = 90 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonSerializer.Deserialize<JsonElement>(body, _json);
        doc.GetProperty("rotationDegrees").GetInt32().Should().Be(90);
    }

    [Fact]
    public async Task Rotate_NonExistentDesign_Returns404()
    {
        var response = await UserClient().PutAsJsonAsync("/api/banner-builder/99999/rotate",
            new { degrees = 90 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Rotate_OtherUsersDesign_Returns403()
    {
        var response = await UserClient(700).PutAsJsonAsync("/api/banner-builder/303/rotate",
            new { degrees = 90 });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── GET /api/banner-builder/{id}/preview ──────────────────────────────────

    [Fact]
    public async Task GetPreview_NoPreviewStoragePath_Returns404()
    {
        // Design 301 has no PreviewStoragePath
        var response = await _factory.CreateClient().GetAsync("/api/banner-builder/301/preview");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPreview_HasPreviewPathButFileNotOnDisk_Returns404()
    {
        // Design 305 has PreviewStoragePath set, but file doesn't exist on disk in tests
        var response = await _factory.CreateClient().GetAsync("/api/banner-builder/305/preview");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPreview_NonExistentDesign_Returns404()
    {
        var response = await _factory.CreateClient().GetAsync("/api/banner-builder/99999/preview");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Rotate with PreviewStoragePath set ────────────────────────────────────

    [Fact]
    public async Task Rotate_DesignWithPreview_RegeneratesPreview()
    {
        // Design 305 has PreviewStoragePath set → should trigger GeneratePreviewAsync mock
        var response = await UserClient(700).PutAsJsonAsync("/api/banner-builder/305/rotate",
            new { degrees = 90 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // Verify the mock was called with the preview path
        _factory.ImageProcessingMock.Verify(
            m => m.GeneratePreviewAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }
}
