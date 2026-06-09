using BannerShop.Api.Models.DesignRequests;
using BannerShop.Api.Services.AiCredits;
using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Api.Services.DesignRequests;
using BannerShop.Api.Services.Email;
using BannerShop.Api.Services.Orders.Stripe;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace BannerShop.Tests;

/// <summary>
/// Tests for POST /design-requests/{id}/generations/{generationId}/activate (BANNERSH-84).
/// Exercises <see cref="DesignRequestService.ActivateGenerationAsync"/> directly.
/// </summary>
public class ActivateGenerationTests
{
    private static BannerFileStorage MakeStorage() =>
        new(Options.Create(new FileStorageOptions
        {
            LocalRoot = Path.Combine(Path.GetTempPath(), "bs-activate-" + Guid.NewGuid().ToString("N")),
            PublicBaseUrl = "/files"
        }));

    private static DesignRequestService MakeService(BannerShop.Infrastructure.Data.BannerShopDbContext db)
    {
        var stripe = new Mock<IStripePaymentService>();
        var queue = new Mock<IDesignRequestJobQueue>();
        var credits = new Mock<IAiCreditService>();
        var images = new Mock<IImageProcessingService>();
        var email = new Mock<IEmailService>();
        var pricing = new BannerShop.Api.Services.PricingService(db);
        return new DesignRequestService(
            db, stripe.Object, queue.Object, MakeStorage(),
            images.Object, email.Object, credits.Object,
            pricing, NullLogger<DesignRequestService>.Instance);
    }

    private static async Task SeedAsync(BannerShop.Infrastructure.Data.BannerShopDbContext db)
    {
        db.Users.Add(DbHelper.MakeUser(1));
        db.BannerTemplates.Add(new BannerTemplate
        {
            Id = 1, Category = BannerTemplateCategory.Birthday,
            NameNb = "Bursdag", NameEn = "Birthday", SortOrder = 10
        });
        await db.SaveChangesAsync();
    }

    private static DesignRequest MakeAiRequest(
        int userId = 1, DesignRequestStatus status = DesignRequestStatus.AwaitingApproval)
        => new DesignRequest
        {
            UserId = userId,
            BannerTemplateId = 1,
            Mode = DesignRequestMode.Ai,
            Status = status,
            Language = "nb",
            PersonName = "Ola",
            TextContent = "Gratulerer",
            ThemeDescription = "tropisk",
            AspectRatio = "16:9",
            PriceNok = 0m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    private static BannerGeneration MakeCompletedGen(
        int requestId, string path, bool isActive = false, DateTime? created = null)
        => new BannerGeneration
        {
            DesignRequestId = requestId,
            Status = BannerGenerationStatus.Completed,
            StoragePath = path,
            CroppedStoragePath = path,
            IsActive = isActive,
            CreatedAt = created ?? DateTime.UtcNow,
            CompletedAt = (created ?? DateTime.UtcNow).AddSeconds(20)
        };

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ActivateGeneration_switches_active_and_updates_request_paths()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var r = MakeAiRequest();
        r.AiResultStoragePath = "designs/1/old.png";
        r.FinalCroppedStoragePath = "designs/1/old.png";
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();

        var gOld = MakeCompletedGen(r.Id, "designs/1/old.png", isActive: true, created: DateTime.UtcNow.AddMinutes(-2));
        var gNew = MakeCompletedGen(r.Id, "designs/1/new.png", isActive: false, created: DateTime.UtcNow);
        db.BannerGenerations.AddRange(gOld, gNew);
        // Note: the AwaitingApproval pivot points at gOld currently.
        r.CurrentGenerationId = gOld.Id;
        await db.SaveChangesAsync();

        var svc = MakeService(db);
        var result = await svc.ActivateGenerationAsync(r.Id, gOld.Id, 1, CancellationToken.None);
        result.Success.Should().BeTrue("activating the already-active gen is a no-op but allowed");

        // Switch to the new generation.
        result = await svc.ActivateGenerationAsync(r.Id, gNew.Id, 1, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Detail.Should().NotBeNull();

        var saved = db.DesignRequests.Find(r.Id)!;
        saved.AiResultStoragePath.Should().Be("designs/1/new.png");
        saved.FinalCroppedStoragePath.Should().Be("designs/1/new.png");
        saved.CurrentGenerationId.Should().Be(gNew.Id);
        saved.Status.Should().Be(DesignRequestStatus.AwaitingApproval);

        var generations = db.BannerGenerations.Where(g => g.DesignRequestId == r.Id).ToList();
        generations.Single(g => g.Id == gOld.Id).IsActive.Should().BeFalse();
        generations.Single(g => g.Id == gNew.Id).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task ActivateGeneration_from_Failed_status_revives_to_AwaitingApproval()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var r = MakeAiRequest(status: DesignRequestStatus.Failed);
        r.LastError = "some failure";
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();

        var g = MakeCompletedGen(r.Id, "designs/1/older.png");
        db.BannerGenerations.Add(g);
        await db.SaveChangesAsync();

        var svc = MakeService(db);
        var result = await svc.ActivateGenerationAsync(r.Id, g.Id, 1, CancellationToken.None);

        result.Success.Should().BeTrue();
        var saved = db.DesignRequests.Find(r.Id)!;
        saved.Status.Should().Be(DesignRequestStatus.AwaitingApproval);
        saved.LastError.Should().BeNull();
        saved.CurrentGenerationId.Should().Be(g.Id);
    }

    // ── Guard conditions ────────────────────────────────────────────────────

    [Fact]
    public async Task ActivateGeneration_returns_404_when_request_missing()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var svc = MakeService(db);

        var result = await svc.ActivateGenerationAsync(9999, 1, 1, CancellationToken.None);
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task ActivateGeneration_rejects_other_user()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        db.Users.Add(DbHelper.MakeUser(2, "other@example.com"));
        var r = MakeAiRequest(userId: 1);
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();

        var g = MakeCompletedGen(r.Id, "designs/1/x.png");
        db.BannerGenerations.Add(g);
        await db.SaveChangesAsync();

        var svc = MakeService(db);
        var result = await svc.ActivateGenerationAsync(r.Id, g.Id, callerUserId: 2, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Forbidden.");
    }

    [Fact]
    public async Task ActivateGeneration_rejects_when_request_is_approved_or_final()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var r = MakeAiRequest(status: DesignRequestStatus.Final);
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();

        var g = MakeCompletedGen(r.Id, "designs/1/x.png");
        db.BannerGenerations.Add(g);
        await db.SaveChangesAsync();

        var svc = MakeService(db);
        var result = await svc.ActivateGenerationAsync(r.Id, g.Id, 1, CancellationToken.None);
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ActivateGeneration_rejects_unfinished_generation()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var r = MakeAiRequest();
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();

        var pending = new BannerGeneration
        {
            DesignRequestId = r.Id,
            Status = BannerGenerationStatus.Pending,
            IsActive = false,
            CreatedAt = DateTime.UtcNow
        };
        db.BannerGenerations.Add(pending);
        await db.SaveChangesAsync();

        var svc = MakeService(db);
        var result = await svc.ActivateGenerationAsync(r.Id, pending.Id, 1, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("completed");
    }

    [Fact]
    public async Task ActivateGeneration_rejects_manual_mode()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var r = MakeAiRequest();
        r.Mode = DesignRequestMode.Manual;
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();

        var g = MakeCompletedGen(r.Id, "designs/1/x.png");
        db.BannerGenerations.Add(g);
        await db.SaveChangesAsync();

        var svc = MakeService(db);
        var result = await svc.ActivateGenerationAsync(r.Id, g.Id, 1, CancellationToken.None);
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("AI");
    }
}
