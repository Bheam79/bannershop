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
/// Tests for POST /design-requests/{id}/regenerate (BANNERSH-66).
/// Exercises <see cref="DesignRequestService.RegenerateAsync"/> directly.
/// </summary>
public class RegenerateAiTests
{
    private static BannerFileStorage MakeStorage() =>
        new(Options.Create(new FileStorageOptions
        {
            LocalRoot = Path.Combine(Path.GetTempPath(), "bs-regen-" + Guid.NewGuid().ToString("N")),
            PublicBaseUrl = "/files"
        }));

    private static (DesignRequestService svc, Mock<IDesignRequestJobQueue> queue, Mock<IAiCreditService> credits)
        MakeService(BannerShop.Infrastructure.Data.BannerShopDbContext db, bool hasCredits = true)
    {
        var stripe = new Mock<IStripePaymentService>();
        var queue = new Mock<IDesignRequestJobQueue>();
        queue.Setup(q => q.EnqueueAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .Returns(ValueTask.CompletedTask);

        var credits = new Mock<IAiCreditService>();
        credits.Setup(c => c.TryConsumeAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(hasCredits);
        credits.Setup(c => c.GetBalanceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(hasCredits ? 4 : 0);

        var images = new Mock<IImageProcessingService>();
        var email = new Mock<IEmailService>();

        var svc = new DesignRequestService(
            db, stripe.Object, queue.Object, MakeStorage(),
            images.Object, email.Object, credits.Object,
            NullLogger<DesignRequestService>.Instance);
        return (svc, queue, credits);
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

    private static DesignRequest MakeAiRequest(int userId = 1, DesignRequestStatus status = DesignRequestStatus.AwaitingApproval)
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

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RegenerateAsync_consumes_credit_and_returns_202_with_generationId()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var req = MakeAiRequest();
        db.DesignRequests.Add(req);
        await db.SaveChangesAsync();

        var (svc, queue, credits) = MakeService(db, hasCredits: true);

        var result = await svc.RegenerateAsync(req.Id, 1, new RegenerateAiRequestDto(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.StatusCode.Should().Be(202);
        result.GenerationId.Should().BeGreaterThan(0);
        result.CreditsRemaining.Should().Be(4);

        credits.Verify(c => c.TryConsumeAsync(1, 1, It.IsAny<CancellationToken>()), Times.Once);
        queue.Verify(q => q.EnqueueAsync(req.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegenerateAsync_creates_pending_BannerGeneration_row()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var req = MakeAiRequest();
        db.DesignRequests.Add(req);
        await db.SaveChangesAsync();

        var (svc, _, _) = MakeService(db, hasCredits: true);
        var result = await svc.RegenerateAsync(req.Id, 1, new RegenerateAiRequestDto(), CancellationToken.None);

        var gen = db.BannerGenerations.SingleOrDefault(g => g.Id == result.GenerationId);
        gen.Should().NotBeNull();
        gen!.Status.Should().Be(BannerGenerationStatus.Pending);
        gen.DesignRequestId.Should().Be(req.Id);
        gen.IsActive.Should().BeFalse(); // pipeline sets it active
    }

    [Fact]
    public async Task RegenerateAsync_resets_status_to_InProgress()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var req = MakeAiRequest(status: DesignRequestStatus.AwaitingApproval);
        db.DesignRequests.Add(req);
        await db.SaveChangesAsync();

        var (svc, _, _) = MakeService(db, hasCredits: true);
        await svc.RegenerateAsync(req.Id, 1, new RegenerateAiRequestDto(), CancellationToken.None);

        db.DesignRequests.Find(req.Id)!.Status.Should().Be(DesignRequestStatus.InProgress);
    }

    [Fact]
    public async Task RegenerateAsync_applies_text_and_theme_overrides()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var req = MakeAiRequest();
        db.DesignRequests.Add(req);
        await db.SaveChangesAsync();

        var (svc, _, _) = MakeService(db, hasCredits: true);
        await svc.RegenerateAsync(req.Id, 1, new RegenerateAiRequestDto
        {
            TextContent = "Ny tekst",
            ThemeDescription = "Nytt tema"
        }, CancellationToken.None);

        var saved = db.DesignRequests.Find(req.Id)!;
        saved.TextContent.Should().Be("Ny tekst");
        saved.ThemeDescription.Should().Be("Nytt tema");
    }

    [Fact]
    public async Task RegenerateAsync_preserves_existing_values_when_overrides_are_null()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var req = MakeAiRequest();
        db.DesignRequests.Add(req);
        await db.SaveChangesAsync();

        var (svc, _, _) = MakeService(db, hasCredits: true);
        await svc.RegenerateAsync(req.Id, 1, new RegenerateAiRequestDto(), CancellationToken.None);

        var saved = db.DesignRequests.Find(req.Id)!;
        saved.TextContent.Should().Be("Gratulerer");
        saved.ThemeDescription.Should().Be("tropisk");
    }

    // ── 402 paywall ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RegenerateAsync_returns_402_when_no_credits()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var req = MakeAiRequest();
        db.DesignRequests.Add(req);
        await db.SaveChangesAsync();

        var (svc, queue, _) = MakeService(db, hasCredits: false);
        var result = await svc.RegenerateAsync(req.Id, 1, new RegenerateAiRequestDto(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(402);
        result.CreditsRemaining.Should().Be(0);

        // Should NOT have enqueued a job or created a BannerGeneration row.
        queue.Verify(q => q.EnqueueAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        db.BannerGenerations.Should().BeEmpty();
    }

    // ── Guard conditions ────────────────────────────────────────────────────

    [Fact]
    public async Task RegenerateAsync_returns_404_for_missing_request()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);

        var (svc, _, _) = MakeService(db);
        var result = await svc.RegenerateAsync(9999, 1, new RegenerateAiRequestDto(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task RegenerateAsync_returns_403_for_wrong_user()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        db.Users.Add(DbHelper.MakeUser(2, "other@example.com"));
        await db.SaveChangesAsync();

        var req = MakeAiRequest(userId: 1);
        db.DesignRequests.Add(req);
        await db.SaveChangesAsync();

        var (svc, _, _) = MakeService(db);
        var result = await svc.RegenerateAsync(req.Id, callerUserId: 2, new RegenerateAiRequestDto(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task RegenerateAsync_rejects_non_AI_requests()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);

        var req = new DesignRequest
        {
            UserId = 1, BannerTemplateId = 1, Mode = DesignRequestMode.Manual,
            Status = DesignRequestStatus.AwaitingApproval,
            Language = "nb", PersonName = "Ola", TextContent = "Hi",
            ThemeDescription = "x", AspectRatio = "16:9",
            PriceNok = 495m, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.DesignRequests.Add(req);
        await db.SaveChangesAsync();

        var (svc, _, _) = MakeService(db);
        var result = await svc.RegenerateAsync(req.Id, 1, new RegenerateAiRequestDto(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Theory]
    [InlineData(DesignRequestStatus.Approved)]
    [InlineData(DesignRequestStatus.Final)]
    [InlineData(DesignRequestStatus.Pending)]
    public async Task RegenerateAsync_rejects_non_regeneratable_statuses(DesignRequestStatus status)
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);

        var req = MakeAiRequest(status: status);
        db.DesignRequests.Add(req);
        await db.SaveChangesAsync();

        var (svc, _, _) = MakeService(db);
        var result = await svc.RegenerateAsync(req.Id, 1, new RegenerateAiRequestDto(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }
}
