using System.Net;
using System.Text;
using BannerShop.Api.Models.DesignRequests;
using BannerShop.Api.Services.AiCredits;
using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Api.Services.DesignRequests;
using BannerShop.Api.Services.DesignRequests.Replicate;
using BannerShop.Api.Services.Email;
using BannerShop.Api.Services.Orders.Stripe;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Infrastructure.Data;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace BannerShop.Tests;

/// <summary>
/// Exercises <see cref="AdminDesignRequestService.UpscaleFinalAsync"/> (BANNERSH-57)
/// end-to-end, including a REAL <see cref="RealEsrganUpscalingService"/> wired to a
/// scripted HTTP handler (same pattern as RealEsrganUpscalingServiceTests) rather than
/// a mock, since the field is a sealed concrete class (not the IUpscalingService
/// interface) and therefore can't be Moq'd. Covers the not-configured/invalid-scale/
/// not-found/no-image/missing-file guard chain plus the full success path: source-path
/// preference (FinalCropped > AiResult), the new persisted file + FinalCroppedStoragePath
/// repoint, the active-BannerGeneration CroppedStoragePath mirror, and upscaler-exception
/// translation to a Fail result.
/// </summary>
public class UpscaleFinalAsyncTests
{
    private static BannerFileStorage MakeStorage() =>
        new(Options.Create(new FileStorageOptions
        {
            LocalRoot = Path.Combine(Path.GetTempPath(), "bs-upscale-final-" + Guid.NewGuid().ToString("N")),
            PublicBaseUrl = "/files"
        }));

    private static AdminDesignRequestService MakeService(
        BannerShopDbContext db, BannerFileStorage storage, RealEsrganUpscalingService? upscaler)
    {
        var email = new Mock<IEmailService>().Object;
        var stripe = new Mock<IStripePaymentService>().Object;
        var queue = new Mock<IDesignRequestJobQueue>().Object;
        var images = new Mock<IImageProcessingService>().Object;
        var credits = new Mock<IAiCreditService>().Object;
        var pricing = new BannerShop.Api.Services.PricingService(db);
        var baseSvc = new DesignRequestService(
            db, stripe, queue, storage, images, email, credits, pricing,
            NullLogger<DesignRequestService>.Instance);

        return new AdminDesignRequestService(
            db, storage, email, baseSvc, NullLogger<AdminDesignRequestService>.Instance, upscaler);
    }

    private static RealEsrganUpscalingService MakeUpscaler(
        HttpMessageHandler handler, int maxPollSeconds = 5, int pollIntervalMs = 5)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.replicate.test") };
        var opts = Options.Create(new ReplicateOptions
        {
            ApiToken = "r8-test-token",
            BaseUrl = "https://api.replicate.test",
            TimeoutSeconds = 5,
            PollIntervalMs = pollIntervalMs,
            MaxPollSeconds = maxPollSeconds
        });
        return new RealEsrganUpscalingService(http, opts, NullLogger<RealEsrganUpscalingService>.Instance);
    }

    private static async Task SeedAsync(BannerShopDbContext db)
    {
        db.BannerTemplates.Add(new BannerTemplate
        {
            Id = 1, Category = BannerTemplateCategory.Birthday,
            NameNb = "Bursdag", NameEn = "Birthday", SortOrder = 10
        });
        await db.SaveChangesAsync();
    }

    private static DesignRequest MakeRequest(
        int? userId = 1, string? finalCropped = null, string? aiResult = null)
        => new DesignRequest
        {
            UserId = userId,
            BannerTemplateId = 1,
            Mode = DesignRequestMode.Ai,
            Language = "nb",
            PersonName = "Ola",
            TextContent = "Gratulerer",
            ThemeDescription = "tropisk",
            AspectRatio = "16:9",
            Status = DesignRequestStatus.Final,
            FinalCroppedStoragePath = finalCropped,
            AiResultStoragePath = aiResult,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    private static string WriteSourceFile(BannerFileStorage storage, int? userId, byte[] bytes, string ext = ".png")
    {
        var dir = storage.EnsureUserDirectory(userId);
        var fileName = $"src_{Guid.NewGuid():N}{ext}";
        File.WriteAllBytes(Path.Combine(dir, fileName), bytes);
        return BannerFileStorage.RelativePathFor(userId, fileName);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>Scripts a successful create -> poll(succeeded) -> download round-trip.</summary>
    private static ScriptedHandler SuccessHandler(string predictionId, byte[] outputBytes)
        => new(req =>
        {
            if (req.RequestUri!.Host == "replicate.delivery")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(outputBytes) };
            return req.Method == HttpMethod.Post
                ? Json(HttpStatusCode.OK, $$"""{ "id": "{{predictionId}}" }""")
                : Json(HttpStatusCode.OK,
                    $$"""{ "id": "{{predictionId}}", "status": "succeeded", "output": "https://replicate.delivery/out/r.png" }""");
        });

    // -- guard chain (no successful upscaler call reached) -----------------------

    [Fact]
    public async Task UpscalerNotConfigured_ReturnsFail()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var svc = MakeService(db, MakeStorage(), upscaler: null);

        var result = await svc.UpscaleFinalAsync(1, 4);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not configured");
    }

    [Fact]
    public async Task InvalidScale_ReturnsFail_WithoutCallingReplicate()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var handler = new ScriptedHandler(_ => throw new InvalidOperationException("should not be called"));
        var svc = MakeService(db, MakeStorage(), MakeUpscaler(handler));

        var result = await svc.UpscaleFinalAsync(1, scale: 3);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Scale must be 2 or 4.");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task NotFound_ReturnsFail()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var handler = new ScriptedHandler(_ => throw new InvalidOperationException("should not be called"));
        var svc = MakeService(db, MakeStorage(), MakeUpscaler(handler));

        var result = await svc.UpscaleFinalAsync(999, 4);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Design request not found.");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task NoImageToUpscaleYet_ReturnsFail()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var r = MakeRequest(finalCropped: null, aiResult: null);
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var handler = new ScriptedHandler(_ => throw new InvalidOperationException("should not be called"));
        var svc = MakeService(db, MakeStorage(), MakeUpscaler(handler));

        var result = await svc.UpscaleFinalAsync(r.Id, 4);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Design request has no image to upscale yet.");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SourceFileMissingOnDisk_ReturnsFail()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var r = MakeRequest(finalCropped: "banner-builder/1/does-not-exist.png");
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var handler = new ScriptedHandler(_ => throw new InvalidOperationException("should not be called"));
        var svc = MakeService(db, MakeStorage(), MakeUpscaler(handler));

        var result = await svc.UpscaleFinalAsync(r.Id, 4);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Source image missing on disk");
        handler.Requests.Should().BeEmpty();
    }

    // -- success path --------------------------------------------------------------

    [Fact]
    public async Task Success_PrefersFinalCroppedOverAiResult_AndPersistsNewFile()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var storage = MakeStorage();
        var croppedRel = WriteSourceFile(storage, 1, new byte[] { 1, 2, 3 });
        var aiRel = WriteSourceFile(storage, 1, new byte[] { 9, 9 });
        var r = MakeRequest(userId: 1, finalCropped: croppedRel, aiResult: aiRel);
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var outputBytes = new byte[] { 4, 5, 6, 7 };
        var handler = SuccessHandler("pred_1", outputBytes);
        var svc = MakeService(db, storage, MakeUpscaler(handler));

        var result = await svc.UpscaleFinalAsync(r.Id, 4);

        result.Success.Should().BeTrue();
        var reloaded = await db.DesignRequests.FindAsync(r.Id);
        reloaded!.FinalCroppedStoragePath.Should().NotBe(croppedRel);
        reloaded.FinalCroppedStoragePath.Should().Contain($"design_{r.Id}_");
        reloaded.FinalCroppedStoragePath.Should().Contain("_x4");
        var newAbs = storage.AbsolutePathFor(reloaded.FinalCroppedStoragePath!);
        File.Exists(newAbs).Should().BeTrue();
        (await File.ReadAllBytesAsync(newAbs)).Should().Equal(outputBytes);
        // original source file must not be clobbered (BANNERSH-57: kept for comparison)
        File.Exists(storage.AbsolutePathFor(croppedRel)).Should().BeTrue();
        var createReq = handler.Requests.Single(rq => rq.Method == HttpMethod.Post);
        (await createReq.Content!.ReadAsStringAsync()).Should().Contain("\"scale\":4");
    }

    [Fact]
    public async Task Success_FallsBackToAiResultStoragePath_WhenFinalCroppedBlank()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var storage = MakeStorage();
        var aiRel = WriteSourceFile(storage, 1, new byte[] { 8, 8 });
        var r = MakeRequest(userId: 1, finalCropped: null, aiResult: aiRel);
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var handler = SuccessHandler("pred_2", new byte[] { 1 });
        var svc = MakeService(db, storage, MakeUpscaler(handler));

        var result = await svc.UpscaleFinalAsync(r.Id, 2);

        result.Success.Should().BeTrue();
        var reloaded = await db.DesignRequests.FindAsync(r.Id);
        reloaded!.FinalCroppedStoragePath.Should().NotBeNullOrEmpty();
        reloaded.FinalCroppedStoragePath.Should().Contain("_x2");
    }

    [Fact]
    public async Task Success_UpdatesActiveGenerationCroppedStoragePath()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var storage = MakeStorage();
        var croppedRel = WriteSourceFile(storage, 1, new byte[] { 1 });
        var r = MakeRequest(userId: 1, finalCropped: croppedRel);
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var activeGen = new BannerGeneration
        {
            DesignRequestId = r.Id,
            Status = BannerGenerationStatus.Completed,
            IsActive = true,
            CroppedStoragePath = croppedRel,
            CreatedAt = DateTime.UtcNow
        };
        var inactiveGen = new BannerGeneration
        {
            DesignRequestId = r.Id,
            Status = BannerGenerationStatus.Completed,
            IsActive = false,
            CroppedStoragePath = "banner-builder/1/other.png",
            CreatedAt = DateTime.UtcNow
        };
        db.BannerGenerations.AddRange(activeGen, inactiveGen);
        await db.SaveChangesAsync();
        var handler = SuccessHandler("pred_3", new byte[] { 2 });
        var svc = MakeService(db, storage, MakeUpscaler(handler));

        var result = await svc.UpscaleFinalAsync(r.Id, 4);

        result.Success.Should().BeTrue();
        var reloadedRequest = await db.DesignRequests.FindAsync(r.Id);
        var reloadedActive = await db.BannerGenerations.FindAsync(activeGen.Id);
        var reloadedInactive = await db.BannerGenerations.FindAsync(inactiveGen.Id);
        reloadedActive!.CroppedStoragePath.Should().Be(reloadedRequest!.FinalCroppedStoragePath);
        reloadedInactive!.CroppedStoragePath.Should().Be("banner-builder/1/other.png", "only the active generation should be touched");
    }

    [Fact]
    public async Task Success_NoActiveGeneration_StillSucceeds()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var storage = MakeStorage();
        var croppedRel = WriteSourceFile(storage, 1, new byte[] { 1 });
        var r = MakeRequest(userId: 1, finalCropped: croppedRel);
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var handler = SuccessHandler("pred_4", new byte[] { 3 });
        var svc = MakeService(db, storage, MakeUpscaler(handler));

        var result = await svc.UpscaleFinalAsync(r.Id, 4);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task AnonymousDesignRequest_PersistsUnderUserZeroDirectory()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var storage = MakeStorage();
        var croppedRel = WriteSourceFile(storage, null, new byte[] { 1 });
        var r = MakeRequest(userId: null, finalCropped: croppedRel);
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var handler = SuccessHandler("pred_5", new byte[] { 4 });
        var svc = MakeService(db, storage, MakeUpscaler(handler));

        var result = await svc.UpscaleFinalAsync(r.Id, 4);

        result.Success.Should().BeTrue();
        var reloaded = await db.DesignRequests.FindAsync(r.Id);
        reloaded!.FinalCroppedStoragePath.Should().StartWith("banner-builder/0/");
    }

    // -- upscaler failure ------------------------------------------------------------

    [Fact]
    public async Task UpscalerThrows_ReturnsFailWithMessage_AndLeavesRequestUntouched()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var storage = MakeStorage();
        var croppedRel = WriteSourceFile(storage, 1, new byte[] { 1 });
        var r = MakeRequest(userId: 1, finalCropped: croppedRel);
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var handler = new ScriptedHandler(req => req.Method == HttpMethod.Post
            ? Json(HttpStatusCode.OK, """{ "id": "pred_6" }""")
            : Json(HttpStatusCode.OK, """{ "id": "pred_6", "status": "failed", "error": "NSFW content detected." }"""));
        var svc = MakeService(db, storage, MakeUpscaler(handler));

        var result = await svc.UpscaleFinalAsync(r.Id, 4);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Upscale failed:");
        result.Error.Should().Contain("NSFW content detected.");
        var reloaded = await db.DesignRequests.FindAsync(r.Id);
        reloaded!.FinalCroppedStoragePath.Should().Be(croppedRel, "a failed upscale must not repoint the storage path");
    }

    // -- helpers ------------------------------------------------------------------

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();

        public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }
}
