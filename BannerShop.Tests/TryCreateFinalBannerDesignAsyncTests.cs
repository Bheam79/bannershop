using BannerShop.Api.Services.AiCredits;
using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Api.Services.DesignRequests;
using BannerShop.Api.Services.Email;
using BannerShop.Api.Services.Orders.Stripe;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace BannerShop.Tests;

/// <summary>
/// Tests for <see cref="DesignRequestService.TryCreateFinalBannerDesignAsync"/> — the
/// internal helper that turns an approved AI/manual DesignRequest's final image into a
/// BannerDesign row so the customer can add it to the print cart. Had zero direct test
/// coverage (only reached indirectly through pipeline/approve mocks that stub
/// IImageProcessingService away). Uses the real <see cref="ImageProcessingService"/> and a
/// real temp-dir <see cref="BannerFileStorage"/> so the actual image-dimension read and
/// storage-path resolution are exercised, not mocked.
/// </summary>
public class TryCreateFinalBannerDesignAsyncTests : IDisposable
{
    private readonly string _root;
    private readonly BannerFileStorage _storage;

    public TryCreateFinalBannerDesignAsyncTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bs-final-design-tests-" + Guid.NewGuid().ToString("N"));
        _storage = new BannerFileStorage(Options.Create(new FileStorageOptions
        {
            LocalRoot = _root,
            PublicBaseUrl = "/files"
        }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private DesignRequestService MakeService(BannerShop.Infrastructure.Data.BannerShopDbContext db)
    {
        var stripe = new Mock<IStripePaymentService>().Object;
        var queue = new Mock<IDesignRequestJobQueue>().Object;
        var email = new Mock<IEmailService>().Object;
        var credits = new Mock<IAiCreditService>().Object;
        var pricing = new BannerShop.Api.Services.PricingService(db);
        return new DesignRequestService(
            db, stripe, queue, _storage, new ImageProcessingService(), email, credits, pricing,
            NullLogger<DesignRequestService>.Instance);
    }

    /// <summary>Writes a real PNG under the storage root and returns its relative storage path.</summary>
    private string WriteImage(int widthPx, int heightPx, string fileName = "final.png")
    {
        var relPath = $"design-final/{fileName}";
        var abs = _storage.AbsolutePathFor(relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        using var img = new Image<Rgba32>(widthPx, heightPx, Color.White);
        img.Save(abs, new PngEncoder());
        return relPath;
    }

    private static DesignRequest MakeRequest(
        string? aspectRatio = "16:9",
        string? finalCropped = null,
        string? designerPreview = null,
        string? aiResult = null,
        string? aiPreview = null,
        int? userId = 1)
        => new DesignRequest
        {
            UserId = userId,
            BannerTemplateId = 1,
            Mode = DesignRequestMode.Ai,
            Language = "nb",
            PersonName = "Ola",
            TextContent = "Gratulerer",
            ThemeDescription = "tropisk",
            AspectRatio = aspectRatio,
            Status = DesignRequestStatus.Pending,
            FinalCroppedStoragePath = finalCropped,
            DesignerPreviewPath = designerPreview,
            AiResultStoragePath = aiResult,
            AiPreviewPath = aiPreview,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    [Fact]
    public async Task AlreadyHasFinalBannerDesign_IsIdempotent_NoNewDesignCreated()
    {
        using var db = DbHelper.CreateInMemory();
        db.Users.Add(DbHelper.MakeUser(1));
        var r = MakeRequest(finalCropped: WriteImage(2000, 1000));
        r.FinalBannerDesignId = 999;
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        await svc.TryCreateFinalBannerDesignAsync(r, CancellationToken.None);

        (await db.BannerDesigns.CountAsync()).Should().Be(0);
        r.FinalBannerDesignId.Should().Be(999);
    }

    [Fact]
    public async Task NoFinalAssetPath_SkipsWithoutThrowing()
    {
        using var db = DbHelper.CreateInMemory();
        db.Users.Add(DbHelper.MakeUser(1));
        var r = MakeRequest();
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        await svc.TryCreateFinalBannerDesignAsync(r, CancellationToken.None);

        (await db.BannerDesigns.CountAsync()).Should().Be(0);
        r.FinalBannerDesignId.Should().BeNull();
    }

    [Fact]
    public async Task FinalAssetPathSet_ButFileMissingOnDisk_SkipsWithoutThrowing()
    {
        using var db = DbHelper.CreateInMemory();
        db.Users.Add(DbHelper.MakeUser(1));
        var r = MakeRequest(finalCropped: "design-final/does-not-exist.png");
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        await svc.TryCreateFinalBannerDesignAsync(r, CancellationToken.None);

        (await db.BannerDesigns.CountAsync()).Should().Be(0);
        r.FinalBannerDesignId.Should().BeNull();
    }

    [Fact]
    public async Task ValidImage_CreatesBannerDesign_AndStampsFinalBannerDesignId()
    {
        using var db = DbHelper.CreateInMemory();
        db.Users.Add(DbHelper.MakeUser(1));
        var path = WriteImage(2000, 1000); // 2:1 aspect
        var r = MakeRequest(aspectRatio: "16:9", finalCropped: path); // AspectRatio height fallback = 150cm
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        await svc.TryCreateFinalBannerDesignAsync(r, CancellationToken.None);

        r.FinalBannerDesignId.Should().NotBeNull();
        var design = await db.BannerDesigns.FindAsync(r.FinalBannerDesignId!.Value);
        design.Should().NotBeNull();
        design!.WidthPx.Should().Be(2000);
        design.HeightPx.Should().Be(1000);
        design.RotationDegrees.Should().Be(0);
        design.SelectedHeightCm.Should().Be(150);
        design.ComputedWidthCm.Should().Be(300); // 150cm * (2000/1000) aspect
        design.StoragePath.Should().Be(path);
        design.UserId.Should().Be(1);
    }

    [Fact]
    public async Task OverrideHeightCm_TakesPrecedenceOverAspectRatioParsedHeight()
    {
        using var db = DbHelper.CreateInMemory();
        db.Users.Add(DbHelper.MakeUser(1));
        var path = WriteImage(2000, 1000); // 2:1 aspect
        var r = MakeRequest(aspectRatio: "16:9", finalCropped: path);
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        await svc.TryCreateFinalBannerDesignAsync(r, CancellationToken.None, overrideHeightCm: 180);

        var design = await db.BannerDesigns.FindAsync(r.FinalBannerDesignId!.Value);
        design!.SelectedHeightCm.Should().Be(180);
        design.ComputedWidthCm.Should().Be(360); // 180cm * 2
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task NonPositiveOverrideHeightCm_FallsBackToAspectRatioParsedHeight(int badOverride)
    {
        using var db = DbHelper.CreateInMemory();
        db.Users.Add(DbHelper.MakeUser(1));
        var path = WriteImage(2000, 1000);
        var r = MakeRequest(aspectRatio: "18:9", finalCropped: path); // -> height 150
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        await svc.TryCreateFinalBannerDesignAsync(r, CancellationToken.None, overrideHeightCm: badOverride);

        var design = await db.BannerDesigns.FindAsync(r.FinalBannerDesignId!.Value);
        design!.SelectedHeightCm.Should().Be(150);
    }

    [Fact]
    public async Task PathFallbackChain_PrefersFinalCropped_ThenDesignerPreview_ThenAiResult()
    {
        using var db = DbHelper.CreateInMemory();
        db.Users.Add(DbHelper.MakeUser(1));
        var croppedPath = WriteImage(1000, 1000, "cropped.png");
        var previewPath = WriteImage(500, 500, "preview.png");
        var r = MakeRequest(finalCropped: croppedPath, designerPreview: previewPath, aiResult: previewPath);
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        await svc.TryCreateFinalBannerDesignAsync(r, CancellationToken.None);

        var design = await db.BannerDesigns.FindAsync(r.FinalBannerDesignId!.Value);
        design!.StoragePath.Should().Be(croppedPath);
    }

    [Fact]
    public async Task PathFallbackChain_UsesAiResult_WhenCroppedAndDesignerPreviewAreNull()
    {
        using var db = DbHelper.CreateInMemory();
        db.Users.Add(DbHelper.MakeUser(1));
        var aiResultPath = WriteImage(1200, 800, "ai-result.png");
        var r = MakeRequest(aiResult: aiResultPath);
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        await svc.TryCreateFinalBannerDesignAsync(r, CancellationToken.None);

        var design = await db.BannerDesigns.FindAsync(r.FinalBannerDesignId!.Value);
        design!.StoragePath.Should().Be(aiResultPath);
    }

    [Theory]
    [InlineData("photo.jpg", "image/jpeg")]
    [InlineData("photo.jpeg", "image/jpeg")]
    [InlineData("photo.webp", "image/webp")]
    [InlineData("photo.png", "image/png")]
    [InlineData("photo.PNG", "image/png")]
    public async Task ContentType_IsInferredFromStoragePathExtension(string fileName, string expectedContentType)
    {
        using var db = DbHelper.CreateInMemory();
        db.Users.Add(DbHelper.MakeUser(1));
        // Real PNG bytes regardless of the (misleading) extension — ReadDimensionsAsync
        // reads the file header, not the name, so this isolates the extension→ContentType switch.
        var path = WriteImage(400, 300, fileName);
        var r = MakeRequest(finalCropped: path);
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        await svc.TryCreateFinalBannerDesignAsync(r, CancellationToken.None);

        var design = await db.BannerDesigns.FindAsync(r.FinalBannerDesignId!.Value);
        design!.ContentType.Should().Be(expectedContentType);
    }

    [Fact]
    public async Task PreviewStoragePath_PrefersAiPreview_OverDesignerPreview()
    {
        using var db = DbHelper.CreateInMemory();
        db.Users.Add(DbHelper.MakeUser(1));
        var finalPath = WriteImage(1000, 1000, "final.png");
        var r = MakeRequest(finalCropped: finalPath, designerPreview: "design-final/designer-prev.png", aiPreview: "design-final/ai-prev.png");
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        await svc.TryCreateFinalBannerDesignAsync(r, CancellationToken.None);

        var design = await db.BannerDesigns.FindAsync(r.FinalBannerDesignId!.Value);
        design!.PreviewStoragePath.Should().Be("design-final/ai-prev.png");
    }

    [Fact]
    public async Task PreviewStoragePath_IsNull_WhenNeitherAiNorDesignerPreviewSet()
    {
        using var db = DbHelper.CreateInMemory();
        db.Users.Add(DbHelper.MakeUser(1));
        var finalPath = WriteImage(1000, 1000, "final.png");
        var r = MakeRequest(finalCropped: finalPath);
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        await svc.TryCreateFinalBannerDesignAsync(r, CancellationToken.None);

        var design = await db.BannerDesigns.FindAsync(r.FinalBannerDesignId!.Value);
        design!.PreviewStoragePath.Should().BeNull();
    }

    [Fact]
    public async Task AnonymousRequest_NullUserId_DefaultsBannerDesignUserIdToZero()
    {
        using var db = DbHelper.CreateInMemory();
        var finalPath = WriteImage(1000, 1000, "final.png");
        var r = MakeRequest(finalCropped: finalPath, userId: null);
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        await svc.TryCreateFinalBannerDesignAsync(r, CancellationToken.None);

        var design = await db.BannerDesigns.FindAsync(r.FinalBannerDesignId!.Value);
        design!.UserId.Should().Be(0);
    }

    [Fact]
    public async Task CorruptImageFile_DimensionReadFails_SkipsWithoutThrowing()
    {
        using var db = DbHelper.CreateInMemory();
        db.Users.Add(DbHelper.MakeUser(1));
        var relPath = "design-final/corrupt.png";
        var abs = _storage.AbsolutePathFor(relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        await File.WriteAllTextAsync(abs, "not a real image");
        var r = MakeRequest(finalCropped: relPath);
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        await svc.TryCreateFinalBannerDesignAsync(r, CancellationToken.None);

        (await db.BannerDesigns.CountAsync()).Should().Be(0);
        r.FinalBannerDesignId.Should().BeNull();
    }
}
