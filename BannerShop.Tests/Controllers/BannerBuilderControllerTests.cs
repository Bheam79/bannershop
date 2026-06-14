using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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

    // ── POST /api/banner-builder/upload ──────────────────────────────────────
    //
    // Magic-byte fixtures: VerifyMagicBytesAsync in the controller compares the
    // first N bytes of the uploaded stream against the signature for the declared
    // type. JPEG → 0xFF 0xD8 0xFF; PNG → the 8-byte PNG header. FakeBytes() is
    // 12 arbitrary bytes that match no signature.

    private static byte[] JpegBytes() =>
        [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01];

    private static byte[] PngBytes() =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    private static byte[] FakeBytes() =>
        [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B];

    /// <summary>Builds a multipart/form-data body with a single "file" part.</summary>
    private static MultipartFormDataContent MakeUpload(string name, byte[] content, string mime)
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(mime);
        form.Add(fileContent, "file", name);
        return form;
    }

    [Fact]
    public async Task Upload_NoFile_Returns400()
    {
        // Empty multipart body with no parts at all — IFormFile binds to null.
        var response = await _factory.CreateClient().PostAsync(
            "/api/banner-builder/upload", new MultipartFormDataContent());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_DisallowedContentType_Returns400()
    {
        // Valid JPEG magic bytes but the declared MIME isn't in the AcceptedTypes table.
        var form = MakeUpload("test.jpg", JpegBytes(), "text/plain");

        var response = await _factory.CreateClient().PostAsync("/api/banner-builder/upload", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_MagicBytesMismatch_Returns400()
    {
        // Declared as JPEG, but the bytes do not match the JPEG signature.
        var form = MakeUpload("fake.jpg", FakeBytes(), "image/jpeg");

        var response = await _factory.CreateClient().PostAsync("/api/banner-builder/upload", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_AnonymousValidJpeg_Returns200_DesignHasNullUserId()
    {
        var form = MakeUpload("anon.jpg", JpegBytes(), "image/jpeg");

        var response = await _factory.CreateClient().PostAsync("/api/banner-builder/upload", form);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonSerializer.Deserialize<JsonElement>(body, _json);
        doc.GetProperty("widthPx").GetInt32().Should().Be(1920); // mock return value
        var designId = doc.GetProperty("designId").GetInt32();
        designId.Should().BeGreaterThan(0);

        // The persisted BannerDesign should be anonymous (no UserId).
        _factory.SeedDatabase(db =>
        {
            var design = db.BannerDesigns.First(d => d.Id == designId);
            design.UserId.Should().BeNull();
        });
    }

    [Fact]
    public async Task Upload_AuthenticatedValidJpeg_Returns200_DesignHasUserId()
    {
        var form = MakeUpload("mine.jpg", JpegBytes(), "image/jpeg");

        var response = await UserClient(700).PostAsync("/api/banner-builder/upload", form);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonSerializer.Deserialize<JsonElement>(body, _json);
        var designId = doc.GetProperty("designId").GetInt32();

        _factory.SeedDatabase(db =>
        {
            var design = db.BannerDesigns.First(d => d.Id == designId);
            design.UserId.Should().Be(700);
        });
    }

    [Fact]
    public async Task Upload_AuthenticatedValidPng_Returns200()
    {
        var form = MakeUpload("mine.png", PngBytes(), "image/png");

        var response = await UserClient(700).PostAsync("/api/banner-builder/upload", form);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonSerializer.Deserialize<JsonElement>(body, _json);
        doc.GetProperty("designId").GetInt32().Should().BeGreaterThan(0);
    }

    // ── PUT /api/banner-builder/{id}/rotate — edge cases ─────────────────────

    [Fact]
    public async Task Rotate_AnonymousDesign_AnonymousCallerCanRotate_Returns200()
    {
        // Anonymous designs (UserId = null) are open to any caller, including unauthenticated.
        _factory.SeedDatabase(db =>
        {
            if (!db.BannerDesigns.Any(d => d.Id == 310))
            {
                db.BannerDesigns.Add(new BannerDesign
                {
                    Id = 310, UserId = null,
                    OriginalFileName = "anon-rotate.jpg",
                    StoragePath = "banner-builder/0/anon-rotate.jpg",
                    ContentType = "image/jpeg",
                    WidthPx = 1920, HeightPx = 1080,
                    RotationDegrees = 0,
                    SelectedHeightCm = 150,
                    ComputedWidthCm = 267,
                    PreviewStoragePath = null,
                    CreatedAt = DateTime.UtcNow
                });
                db.SaveChanges();
            }
        });

        var response = await _factory.CreateClient().PutAsJsonAsync(
            "/api/banner-builder/310/rotate", new { degrees = 90 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Rotate_AnonymousDesign_DifferentUserCanRotate_Returns200()
    {
        // Same anonymous design as the previous test — another authenticated user can still rotate.
        _factory.SeedDatabase(db =>
        {
            if (!db.BannerDesigns.Any(d => d.Id == 310))
            {
                db.BannerDesigns.Add(new BannerDesign
                {
                    Id = 310, UserId = null,
                    OriginalFileName = "anon-rotate.jpg",
                    StoragePath = "banner-builder/0/anon-rotate.jpg",
                    ContentType = "image/jpeg",
                    WidthPx = 1920, HeightPx = 1080,
                    RotationDegrees = 0,
                    SelectedHeightCm = 150,
                    ComputedWidthCm = 267,
                    PreviewStoragePath = null,
                    CreatedAt = DateTime.UtcNow
                });
                db.SaveChanges();
            }
        });

        var response = await UserClient(701).PutAsJsonAsync(
            "/api/banner-builder/310/rotate", new { degrees = 90 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Rotate_AnotherUsersDesign_AdminCanRotate_Returns200()
    {
        // Design 303 is owned by user 999; an admin caller must be allowed.
        var response = await AdminClient().PutAsJsonAsync(
            "/api/banner-builder/303/rotate", new { degrees = 90 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── GET /api/banner-builder/{id}/preview — ownership behaviour ───────────

    [Fact]
    public async Task GetPreview_AnotherUsersDesign_NoOwnershipCheck_AccessibleToAnyone()
    {
        // Documents the intentional comment in GetPreview: "Preview images (JPEG thumbnails)
        // are not sensitive; no ownership check needed." Even an anonymous caller can pull
        // a preview owned by another user as long as the file is on disk.
        const int designId = 320;
        const string relPath = "banner-builder/700/preview-320.jpg";

        // Resolve the absolute path via the real BannerFileStorage so this test is decoupled
        // from how the storage layer turns relative paths into absolute ones.
        string absPath;
        using (var scope = _factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<BannerFileStorage>();
            absPath = storage.AbsolutePathFor(relPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(absPath)!);
        // Tiny but valid-ish JPEG (header bytes only — the controller streams it back as-is).
        await File.WriteAllBytesAsync(absPath, JpegBytes());

        _factory.SeedDatabase(db =>
        {
            if (!db.BannerDesigns.Any(d => d.Id == designId))
            {
                db.BannerDesigns.Add(new BannerDesign
                {
                    Id = designId, UserId = 700,
                    OriginalFileName = "owned.jpg",
                    StoragePath = "banner-builder/700/owned.jpg",
                    ContentType = "image/jpeg",
                    WidthPx = 1920, HeightPx = 1080,
                    RotationDegrees = 0,
                    SelectedHeightCm = 150,
                    ComputedWidthCm = 267,
                    PreviewStoragePath = relPath,
                    CreatedAt = DateTime.UtcNow
                });
                db.SaveChanges();
            }
        });

        var response = await _factory.CreateClient().GetAsync($"/api/banner-builder/{designId}/preview");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("image/jpeg");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().BeEquivalentTo(JpegBytes());
    }
}

