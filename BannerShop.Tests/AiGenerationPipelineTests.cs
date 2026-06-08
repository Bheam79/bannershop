using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Api.Services.DesignRequests;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Infrastructure.Data;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace BannerShop.Tests;

/// <summary>
/// Unit tests for <see cref="AiGenerationPipeline"/> — the orchestration class that
/// turns a paid AI <see cref="DesignRequest"/> into a generated + cropped print PNG.
///
/// Collaborators are mocked except for the pure <see cref="BannerPromptService"/>
/// helper and the file-system backed <see cref="BannerFileStorage"/> (which is
/// pointed at a per-test temp directory and torn down in <see cref="Dispose"/>).
///
/// See BANNERSH-41 for the test plan and BANNERSH-62 for the implementation task.
/// </summary>
public class AiGenerationPipelineTests : IDisposable
{
    private readonly string _tempDir;
    private readonly BannerFileStorage _storage;
    private readonly List<string> _aiOutputs = new();

    public AiGenerationPipelineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "bannershop-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _storage = new BannerFileStorage(Options.Create(new FileStorageOptions
        {
            LocalRoot = _tempDir,
            PublicBaseUrl = "/files"
        }));
    }

    public void Dispose()
    {
        // Per-test storage root — everything the pipeline wrote lives here.
        try
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch { /* best effort */ }

        // The pipeline normally cleans these up itself via TryDelete, but on
        // failure paths (e.g. the throw test) the raw AI output may linger.
        foreach (var p in _aiOutputs)
        {
            try { if (File.Exists(p)) File.Delete(p); } catch { /* best effort */ }
        }

        GC.SuppressFinalize(this);
    }

    // ───────────────────────────── helpers ─────────────────────────────

    private static async Task SeedAsync(
        BannerShopDbContext db,
        BannerTemplateCategory category = BannerTemplateCategory.Birthday)
    {
        db.Users.Add(DbHelper.MakeUser(1));
        db.BannerTemplates.Add(new BannerTemplate
        {
            Id = 1,
            Category = category,
            NameNb = "Test",
            NameEn = "Test",
            SortOrder = 1
        });
        await db.SaveChangesAsync();
    }

    private static DesignRequest MakeRequest(
        DesignRequestMode mode = DesignRequestMode.Ai,
        DesignRequestStatus status = DesignRequestStatus.InProgress,
        string aspectRatio = "16:9",
        string? uploadedPhotoPath = null)
        => new DesignRequest
        {
            UserId = 1,
            BannerTemplateId = 1,
            Mode = mode,
            Status = status,
            Language = "nb",
            PersonName = "Ola",
            PersonAge = 40,
            TextContent = "Gratulerer",
            ThemeDescription = "tropisk",
            AspectRatio = aspectRatio,
            UploadedPhotoPath = uploadedPhotoPath,
            PriceNok = 95m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    /// <summary>
    /// AI service mock that writes a small real PNG to the system temp dir,
    /// so the downstream <c>File.Copy</c> in the pipeline has a real source.
    /// The path is tracked for Dispose-time cleanup in case the pipeline aborts
    /// before its own TryDelete runs.
    /// </summary>
    private Mock<IAiImageService> MakeAiMock()
    {
        var ai = new Mock<IAiImageService>();
        ai.Setup(s => s.GenerateAsync(It.IsAny<AiImageRequest>(), It.IsAny<CancellationToken>()))
          .Returns<AiImageRequest, CancellationToken>(async (_, ct) =>
          {
              var path = Path.Combine(Path.GetTempPath(), $"airesult_{Guid.NewGuid():N}.png");
              using var img = new Image<Rgba32>(16, 9, new Rgba32(120, 80, 200, 255));
              await img.SaveAsPngAsync(path, ct);
              _aiOutputs.Add(path);
              return new AiImageResult(path, 16, 9);
          });
        return ai;
    }

    private static Mock<IUpscalingService> MakeNoopUpscaleMock()
    {
        var up = new Mock<IUpscalingService>();
        up.Setup(s => s.UpscaleAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
          .Returns<string, int, CancellationToken>((p, _, _) => Task.FromResult(p));
        return up;
    }

    /// <summary>
    /// Image-processing mock with a CenterCropAsync that copies source -> dest so
    /// any downstream code expecting the cropped file to exist on disk is happy.
    /// </summary>
    private static Mock<IImageProcessingService> MakeImagesMock()
    {
        var images = new Mock<IImageProcessingService>();
        images
            .Setup(s => s.CenterCropAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, int, int, CancellationToken>((src, dst, _, _, _) =>
            {
                File.Copy(src, dst, overwrite: true);
                return Task.FromResult((16, 8));
            });
        return images;
    }

    private static Mock<IPromptRefinementService> MakeRefinerMock()
    {
        var refiner = new Mock<IPromptRefinementService>();
        refiner.Setup(s => s.RefineAsync(It.IsAny<PromptRefinementInput>(), It.IsAny<CancellationToken>()))
               .Returns<PromptRefinementInput, CancellationToken>((i, _) => Task.FromResult(i.BasePrompt));
        return refiner;
    }

    private AiGenerationPipeline MakePipeline(
        BannerShopDbContext db,
        IAiImageService ai,
        IImageProcessingService images,
        IUpscalingService? upscaler = null,
        IPromptRefinementService? refiner = null)
        => new AiGenerationPipeline(
            db,
            new BannerPromptService(),
            refiner ?? MakeRefinerMock().Object,
            ai,
            upscaler ?? MakeNoopUpscaleMock().Object,
            images,
            _storage,
            NullLogger<AiGenerationPipeline>.Instance);

    // ───────────────────────────── tests ─────────────────────────────

    // 1. Happy path
    [Fact]
    public async Task HappyPath_AiMode_succeeds_and_populates_paths()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);

        var req = MakeRequest();
        db.DesignRequests.Add(req);
        await db.SaveChangesAsync();

        var ai = MakeAiMock();
        var images = MakeImagesMock();
        var pipeline = MakePipeline(db, ai.Object, images.Object);

        await pipeline.RunAsync(req.Id, CancellationToken.None);

        var saved = await db.DesignRequests.FindAsync(req.Id);
        saved!.Status.Should().Be(DesignRequestStatus.AwaitingApproval);
        saved.AiResultStoragePath.Should().NotBeNullOrEmpty();
        saved.FinalCroppedStoragePath.Should().NotBeNullOrEmpty();
        // 16:9 (non-18:9) skips crop, so the two paths are identical.
        saved.FinalCroppedStoragePath.Should().Be(saved.AiResultStoragePath);
        saved.LastError.Should().BeNull();

        // The persisted file should actually exist on disk under the storage root.
        File.Exists(_storage.AbsolutePathFor(saved.AiResultStoragePath!)).Should().BeTrue();

        ai.Verify(s => s.GenerateAsync(It.IsAny<AiImageRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // 2. Missing DesignRequest
    [Fact]
    public async Task Missing_DesignRequest_returns_early_without_touching_DB()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);

        var ai = MakeAiMock();
        var images = MakeImagesMock();
        var pipeline = MakePipeline(db, ai.Object, images.Object);

        await pipeline.RunAsync(designRequestId: 999_999, CancellationToken.None);

        db.DesignRequests.Count().Should().Be(0);
        ai.Verify(s => s.GenerateAsync(It.IsAny<AiImageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        images.Verify(s => s.CenterCropAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        // No per-user directory should have been created either.
        Directory.EnumerateFileSystemEntries(_tempDir).Should().BeEmpty();
    }

    // 3. Non-AI (Manual) mode guard
    [Fact]
    public async Task Manual_mode_returns_without_generating()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var req = MakeRequest(mode: DesignRequestMode.Manual);
        db.DesignRequests.Add(req);
        await db.SaveChangesAsync();

        var ai = MakeAiMock();
        var images = MakeImagesMock();
        var pipeline = MakePipeline(db, ai.Object, images.Object);

        await pipeline.RunAsync(req.Id, CancellationToken.None);

        ai.Verify(s => s.GenerateAsync(It.IsAny<AiImageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        var saved = await db.DesignRequests.FindAsync(req.Id);
        saved!.Status.Should().Be(DesignRequestStatus.InProgress); // untouched
        saved.AiResultStoragePath.Should().BeNull();
        saved.FinalCroppedStoragePath.Should().BeNull();
    }

    // 4. Truly terminal statuses (Approved, Final, Cancelled) short-circuit without regenerating.
    //    AwaitingApproval is NOT terminal — the /regenerate endpoint resets to InProgress first,
    //    but the pipeline itself now accepts AwaitingApproval requests (to be safe for retries).
    [Theory]
    [InlineData(DesignRequestStatus.Approved)]
    [InlineData(DesignRequestStatus.Final)]
    [InlineData(DesignRequestStatus.Cancelled)]
    public async Task Terminal_status_short_circuits_without_regenerating(DesignRequestStatus status)
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var req = MakeRequest(status: status);
        db.DesignRequests.Add(req);
        await db.SaveChangesAsync();

        var ai = MakeAiMock();
        var images = MakeImagesMock();
        var pipeline = MakePipeline(db, ai.Object, images.Object);

        await pipeline.RunAsync(req.Id, CancellationToken.None);

        ai.Verify(s => s.GenerateAsync(It.IsAny<AiImageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        var saved = await db.DesignRequests.FindAsync(req.Id);
        saved!.Status.Should().Be(status);
    }

    // 5. AI service throws -> Failed + LastError populated
    [Fact]
    public async Task AiImageService_throws_marks_request_Failed_with_LastError()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var req = MakeRequest();
        db.DesignRequests.Add(req);
        await db.SaveChangesAsync();

        var ai = new Mock<IAiImageService>();
        ai.Setup(s => s.GenerateAsync(It.IsAny<AiImageRequest>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("upstream went boom"));

        var images = MakeImagesMock();
        var pipeline = MakePipeline(db, ai.Object, images.Object);

        await pipeline.RunAsync(req.Id, CancellationToken.None);

        var saved = await db.DesignRequests.FindAsync(req.Id);
        saved!.Status.Should().Be(DesignRequestStatus.Failed);
        saved.LastError.Should().NotBeNullOrEmpty();
        saved.LastError.Should().Be("upstream went boom");
        saved.AiResultStoragePath.Should().BeNull();
        saved.FinalCroppedStoragePath.Should().BeNull();

        // Crop is downstream of AI generation, so it must NOT have been invoked.
        images.Verify(s => s.CenterCropAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // 6. Crop applied only for 18:9, skipped for other ratios
    [Theory]
    [InlineData("18:9", 1)]
    [InlineData("16:9", 0)]
    [InlineData("1:1",  0)]
    public async Task CenterCrop_invoked_only_for_18_to_9_ratio(string aspectRatio, int expectedCropCalls)
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var req = MakeRequest(aspectRatio: aspectRatio);
        db.DesignRequests.Add(req);
        await db.SaveChangesAsync();

        var ai = MakeAiMock();
        var images = MakeImagesMock();
        var pipeline = MakePipeline(db, ai.Object, images.Object);

        await pipeline.RunAsync(req.Id, CancellationToken.None);

        images.Verify(
            s => s.CenterCropAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                2, 1, It.IsAny<CancellationToken>()),
            Times.Exactly(expectedCropCalls));

        var saved = await db.DesignRequests.FindAsync(req.Id);
        saved!.Status.Should().Be(DesignRequestStatus.AwaitingApproval);

        if (expectedCropCalls == 0)
            saved.FinalCroppedStoragePath.Should().Be(saved.AiResultStoragePath);
        else
            saved.FinalCroppedStoragePath.Should().NotBe(saved.AiResultStoragePath);
    }

    // 7. Missing reference photo -> pipeline still succeeds, ReferenceImagePath is null
    [Fact]
    public async Task Missing_reference_photo_proceeds_without_portrait()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var req = MakeRequest(uploadedPhotoPath: "banner-builder/1/does-not-exist.png");
        db.DesignRequests.Add(req);
        await db.SaveChangesAsync();

        AiImageRequest? capturedRequest = null;
        var ai = new Mock<IAiImageService>();
        ai.Setup(s => s.GenerateAsync(It.IsAny<AiImageRequest>(), It.IsAny<CancellationToken>()))
          .Returns<AiImageRequest, CancellationToken>(async (r, ct) =>
          {
              capturedRequest = r;
              var path = Path.Combine(Path.GetTempPath(), $"airesult_{Guid.NewGuid():N}.png");
              using var img = new Image<Rgba32>(16, 9, new Rgba32(40, 40, 40, 255));
              await img.SaveAsPngAsync(path, ct);
              _aiOutputs.Add(path);
              return new AiImageResult(path, 16, 9);
          });

        var images = MakeImagesMock();
        var pipeline = MakePipeline(db, ai.Object, images.Object);

        await pipeline.RunAsync(req.Id, CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.ReferenceImagePath.Should().BeNull();

        var saved = await db.DesignRequests.FindAsync(req.Id);
        saved!.Status.Should().Be(DesignRequestStatus.AwaitingApproval);
        saved.LastError.Should().BeNull();
        saved.AiResultStoragePath.Should().NotBeNullOrEmpty();
    }

    // ── BANNERSH-66: BannerGeneration tracking ───────────────────────────────

    // 8. Pipeline creates a BannerGeneration row and sets it IsActive on success
    [Fact]
    public async Task Pipeline_creates_BannerGeneration_row_and_sets_active()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);

        var req = MakeRequest();
        db.DesignRequests.Add(req);
        await db.SaveChangesAsync();

        var ai = MakeAiMock();
        var images = MakeImagesMock();
        var pipeline = MakePipeline(db, ai.Object, images.Object);

        await pipeline.RunAsync(req.Id, CancellationToken.None);

        // Exactly one BannerGeneration row should exist.
        var generations = db.BannerGenerations.Where(g => g.DesignRequestId == req.Id).ToList();
        generations.Should().ContainSingle();

        var gen = generations.Single();
        gen.Status.Should().Be(BannerGenerationStatus.Completed);
        gen.IsActive.Should().BeTrue();
        gen.StoragePath.Should().NotBeNullOrEmpty();
        gen.CroppedStoragePath.Should().NotBeNullOrEmpty();
        gen.CompletedAt.Should().NotBeNull();

        // DesignRequest.CurrentGenerationId should point to the new generation.
        var savedReq = db.DesignRequests.Find(req.Id)!;
        savedReq.CurrentGenerationId.Should().Be(gen.Id);
    }

    // 9. Second pipeline run (regenerate) deactivates the old generation and creates a new active one
    [Fact]
    public async Task Second_pipeline_run_deactivates_old_generation_and_activates_new()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);

        var req = MakeRequest();
        db.DesignRequests.Add(req);
        await db.SaveChangesAsync();

        var ai = MakeAiMock();
        var images = MakeImagesMock();
        var pipeline = MakePipeline(db, ai.Object, images.Object);

        // First run
        await pipeline.RunAsync(req.Id, CancellationToken.None);
        var firstGen = db.BannerGenerations.Single(g => g.DesignRequestId == req.Id);
        firstGen.IsActive.Should().BeTrue();

        // Reset status to InProgress (as the /regenerate endpoint would do).
        var savedReq = db.DesignRequests.Find(req.Id)!;
        savedReq.Status = DesignRequestStatus.InProgress;
        await db.SaveChangesAsync();

        // Second run
        await pipeline.RunAsync(req.Id, CancellationToken.None);

        db.ChangeTracker.Clear(); // refresh from DB
        var allGenerations = db.BannerGenerations.Where(g => g.DesignRequestId == req.Id).ToList();
        allGenerations.Should().HaveCount(2);

        // Only the newest generation should be active.
        allGenerations.Count(g => g.IsActive).Should().Be(1);
        var activeGen = allGenerations.Single(g => g.IsActive);
        activeGen.Id.Should().BeGreaterThan(firstGen.Id); // newer row
    }

    // 10. Pipeline uses a pre-existing Pending generation row (created by the /regenerate endpoint)
    [Fact]
    public async Task Pipeline_uses_existing_pending_generation_row()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);

        var req = MakeRequest();
        db.DesignRequests.Add(req);
        await db.SaveChangesAsync();

        // Simulate /regenerate endpoint pre-creating the row.
        var preCreated = new BannerGeneration
        {
            DesignRequestId = req.Id,
            Status = BannerGenerationStatus.Pending,
            IsActive = false,
            CreatedAt = DateTime.UtcNow
        };
        db.BannerGenerations.Add(preCreated);
        await db.SaveChangesAsync();

        var ai = MakeAiMock();
        var images = MakeImagesMock();
        var pipeline = MakePipeline(db, ai.Object, images.Object);

        await pipeline.RunAsync(req.Id, CancellationToken.None);

        // Should have used the pre-created row (no extra row created).
        var allGenerations = db.BannerGenerations.Where(g => g.DesignRequestId == req.Id).ToList();
        allGenerations.Should().ContainSingle();

        var gen = allGenerations.Single();
        gen.Id.Should().Be(preCreated.Id); // same row used
        gen.Status.Should().Be(BannerGenerationStatus.Completed);
        gen.IsActive.Should().BeTrue();
    }

    // 11. Failed generation creates a BannerGeneration row with Failed status
    [Fact]
    public async Task Failed_generation_creates_failed_BannerGeneration_row()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var req = MakeRequest();
        db.DesignRequests.Add(req);
        await db.SaveChangesAsync();

        var ai = new Mock<IAiImageService>();
        ai.Setup(s => s.GenerateAsync(It.IsAny<AiImageRequest>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("ai exploded"));

        var images = MakeImagesMock();
        var pipeline = MakePipeline(db, ai.Object, images.Object);

        await pipeline.RunAsync(req.Id, CancellationToken.None);

        var gen = db.BannerGenerations.Single(g => g.DesignRequestId == req.Id);
        gen.Status.Should().Be(BannerGenerationStatus.Failed);
        gen.ErrorMessage.Should().Be("ai exploded");
        gen.IsActive.Should().BeTrue(); // still active (the only attempt)
        gen.StoragePath.Should().BeNull();
    }
}
