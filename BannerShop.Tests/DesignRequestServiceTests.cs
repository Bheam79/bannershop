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

public class DesignRequestServiceTests
{
    private static BannerFileStorage MakeStorage() =>
        new(Options.Create(new FileStorageOptions
        {
            LocalRoot = Path.Combine(Path.GetTempPath(), "bs-tests-" + Guid.NewGuid().ToString("N")),
            PublicBaseUrl = "/files"
        }));

    private static (DesignRequestService service, Mock<IStripePaymentService> stripe, Mock<IDesignRequestJobQueue> queue, Mock<IAiCreditService> credits) MakeService(
        BannerShop.Infrastructure.Data.BannerShopDbContext db,
        Action<Mock<IAiCreditService>>? configureCredits = null)
    {
        var stripe = new Mock<IStripePaymentService>();
        stripe.Setup(s => s.CreatePaymentIntentAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new StripeIntentResult("pi_test_123", "pi_test_123_secret"));

        var queue = new Mock<IDesignRequestJobQueue>();
        queue.Setup(q => q.EnqueueAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .Returns(ValueTask.CompletedTask);

        var images = new Mock<IImageProcessingService>();
        var email = new Mock<IEmailService>();
        var credits = new Mock<IAiCreditService>();
        credits.Setup(c => c.IsAnonymousEligibleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(true);
        credits.Setup(c => c.TryConsumeAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(true);
        credits.Setup(c => c.GetBalanceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(5);
        configureCredits?.Invoke(credits);

        // BANNERSH-104: PricingService is injected so CreateManualRequestAsync can compute
        // the physical-banner cost. Tests that don't seed the catalog fall back to 0
        // banner price (the service degrades gracefully — see ResolveBannerProductionCostAsync).
        var pricing = new BannerShop.Api.Services.PricingService(db);
        var svc = new DesignRequestService(db, stripe.Object, queue.Object, MakeStorage(), images.Object, email.Object, credits.Object, pricing, NullLogger<DesignRequestService>.Instance);
        return (svc, stripe, queue, credits);
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

    private static CreateAiDesignRequestDto SampleAiDto(int templateId = 1) =>
        new()
        {
            TemplateId = templateId,
            Language = "nb",
            PersonName = "Ola",
            PersonAge = 40,
            TextContent = "Gratulerer",
            ThemeDescription = "tropisk",
            AspectRatio = "16:9"
        };

    // ── BANNERSH-67: Authenticated path ───────────────────────────────────────

    [Fact]
    public async Task CreateAiRequestAsync_auth_first_call_is_free_and_marks_user()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var (svc, stripe, queue, credits) = MakeService(db);

        var result = await svc.CreateAiRequestAsync(userId: 1, ipAddress: null, SampleAiDto());

        result.Success.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.RequiresAuth.Should().BeFalse();

        var saved = db.DesignRequests.Single();
        saved.Status.Should().Be(DesignRequestStatus.InProgress);
        saved.StripePaymentIntentId.Should().BeNull();
        saved.UserId.Should().Be(1);
        saved.PriceNok.Should().Be(0m);

        db.Users.Single().HasUsedFreeAiGeneration.Should().BeTrue();
        stripe.Verify(s => s.CreatePaymentIntentAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
        queue.Verify(q => q.EnqueueAsync(saved.Id, It.IsAny<CancellationToken>()), Times.Once);
        credits.Verify(c => c.TryConsumeAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAiRequestAsync_auth_second_call_without_credits_returns_402()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        // Pre-mark user as having used their free generation, with 0 credits.
        var user = db.Users.Single();
        user.HasUsedFreeAiGeneration = true;
        user.AiCreditsRemaining = 0;
        await db.SaveChangesAsync();

        var (svc, _, queue, _) = MakeService(db, credits =>
        {
            credits.Setup(c => c.TryConsumeAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(false);
            credits.Setup(c => c.GetBalanceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(0);
        });

        var result = await svc.CreateAiRequestAsync(userId: 1, ipAddress: null, SampleAiDto());

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(402);
        result.Paywall.Should().NotBeNull();
        result.Paywall!.Reason.Should().Be("insufficient_credits");
        result.Paywall.PaywallOptions.Should().NotBeNull();
        db.DesignRequests.Should().BeEmpty();
        queue.Verify(q => q.EnqueueAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAiRequestAsync_auth_third_call_after_credit_grant_succeeds()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var user = db.Users.Single();
        user.HasUsedFreeAiGeneration = true;
        user.AiCreditsRemaining = 5;
        await db.SaveChangesAsync();

        var (svc, _, queue, credits) = MakeService(db);

        var result = await svc.CreateAiRequestAsync(userId: 1, ipAddress: null, SampleAiDto());

        result.Success.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        credits.Verify(c => c.TryConsumeAsync(1, 1, It.IsAny<CancellationToken>()), Times.Once);
        queue.Verify(q => q.EnqueueAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAiRequestAsync_rejects_unknown_template()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var (svc, _, _, _) = MakeService(db);

        var result = await svc.CreateAiRequestAsync(userId: 1, ipAddress: null, SampleAiDto(templateId: 9999));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("template");
    }

    // ── BANNERSH-67: Anonymous path ───────────────────────────────────────────

    [Fact]
    public async Task CreateAiRequestAsync_anonymous_eligible_creates_and_records_usage()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var (svc, stripe, queue, credits) = MakeService(db);

        var result = await svc.CreateAiRequestAsync(userId: null, ipAddress: "1.2.3.4", SampleAiDto());

        result.Success.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.RequiresAuth.Should().BeTrue();

        var saved = db.DesignRequests.Single();
        saved.UserId.Should().BeNull();
        saved.IpAddress.Should().Be("1.2.3.4");
        saved.RegenerationsRemaining.Should().Be(0);
        saved.StripePaymentIntentId.Should().BeNull();
        saved.Status.Should().Be(DesignRequestStatus.InProgress);

        stripe.Verify(s => s.CreatePaymentIntentAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
        queue.Verify(q => q.EnqueueAsync(saved.Id, It.IsAny<CancellationToken>()), Times.Once);
        credits.Verify(c => c.RecordAnonymousUsageAsync("1.2.3.4", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAiRequestAsync_anonymous_ineligible_returns_402_ip_limit()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var (svc, _, queue, credits) = MakeService(db, c =>
        {
            c.Setup(x => x.IsAnonymousEligibleAsync("1.2.3.4", It.IsAny<CancellationToken>()))
             .ReturnsAsync(false);
        });

        var result = await svc.CreateAiRequestAsync(userId: null, ipAddress: "1.2.3.4", SampleAiDto());

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(402);
        result.Paywall!.Reason.Should().Be("ip_limit_reached");
        db.DesignRequests.Should().BeEmpty();
        queue.Verify(q => q.EnqueueAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        credits.Verify(c => c.RecordAnonymousUsageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAiRequestAsync_anonymous_without_ip_returns_400()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var (svc, _, _, _) = MakeService(db);

        var result = await svc.CreateAiRequestAsync(userId: null, ipAddress: null, SampleAiDto());

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("IP");
    }

    // ── BANNERSH-104: Manual flow charges design fee + banner production cost ──

    [Fact]
    public async Task CreateManualRequestAsync_charges_design_fee_plus_banner_cost_when_catalog_seeded()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        DbHelper.SeedPricingParameters(db);
        DbHelper.SeedCatalog(db);
        var (svc, stripe, _, _) = MakeService(db);

        var result = await svc.CreateManualRequestAsync(1, new CreateManualDesignRequestDto
        {
            TemplateId = 1, Language = "nb", PersonName = "Ola", TextContent = "Hi",
            ThemeDescription = "x", AspectRatio = "18:9"
        });

        result.Success.Should().BeTrue();
        result.DesignPriceNok.Should().Be(495m);
        result.BannerPriceNok.Should().BeGreaterThan(0m);
        result.TotalNok.Should().Be(result.DesignPriceNok + result.BannerPriceNok);

        var saved = db.DesignRequests.Single();
        saved.PriceNok.Should().Be(495m);
        saved.BannerPriceNok.Should().Be(result.BannerPriceNok);
        saved.BannerSizeId.Should().NotBeNull();

        // The Stripe charge must match the breakdown — this is the regression we're
        // guarding against (BANNERSH-104: previously only the 495 kr design fee was
        // charged, leaving the physical-banner production cost uncollected).
        stripe.Verify(s => s.CreatePaymentIntentAsync(
            It.IsAny<int>(), 1, result.TotalNok, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateManualRequestAsync_falls_back_to_design_only_when_catalog_empty()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        // No catalog seeded — ResolveBannerProductionCostAsync should degrade to 0.
        var (svc, stripe, _, _) = MakeService(db);

        var result = await svc.CreateManualRequestAsync(1, new CreateManualDesignRequestDto
        {
            TemplateId = 1, Language = "nb", PersonName = "Ola", TextContent = "Hi",
            ThemeDescription = "x", AspectRatio = "16:9"
        });

        result.Success.Should().BeTrue();
        result.DesignPriceNok.Should().Be(495m);
        result.BannerPriceNok.Should().Be(0m);
        result.TotalNok.Should().Be(495m);
        db.DesignRequests.Single().BannerSizeId.Should().BeNull();

        stripe.Verify(s => s.CreatePaymentIntentAsync(
            It.IsAny<int>(), 1, 495m, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── MarkPaidAndEnqueueAsync: Manual mode (495 kr) still triggers, AI is dead-code ──

    [Fact]
    public async Task MarkPaidAndEnqueueAsync_manual_flips_to_InProgress()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var (svc, _, queue, _) = MakeService(db);

        var manualResult = await svc.CreateManualRequestAsync(1, new CreateManualDesignRequestDto
        {
            TemplateId = 1, Language = "nb", PersonName = "Ola", TextContent = "Hi",
            ThemeDescription = "x", AspectRatio = "16:9"
        });
        manualResult.Success.Should().BeTrue();
        // Simulate the prior state — CreateManualRequest sets Pending and StripePaymentIntentId.
        await svc.MarkPaidAndEnqueueAsync("pi_test_123");

        var saved = db.DesignRequests.Single();
        saved.Status.Should().Be(DesignRequestStatus.InProgress);
        // Manual flow never enqueues for AI pipeline — designer handles it.
        queue.Verify(q => q.EnqueueAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MarkPaidAndEnqueueAsync_ai_is_no_op_after_BANNERSH67()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var (svc, _, queue, _) = MakeService(db);

        // Simulate an in-flight legacy AI request that still has a StripePaymentIntentId
        // (e.g. created before BANNERSH-67 deploy).
        var legacy = new BannerShop.Core.Entities.DesignRequest
        {
            UserId = 1,
            BannerTemplateId = 1,
            Mode = DesignRequestMode.Ai,
            Language = "nb",
            PersonName = "Ola",
            TextContent = "Hi",
            ThemeDescription = "x",
            AspectRatio = "16:9",
            Status = DesignRequestStatus.Pending,
            PriceNok = 95m,
            StripePaymentIntentId = "pi_legacy_ai",
            RegenerationsRemaining = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.DesignRequests.Add(legacy);
        await db.SaveChangesAsync();

        await svc.MarkPaidAndEnqueueAsync("pi_legacy_ai");

        // Status should NOT advance — dead-code guard.
        var saved = db.DesignRequests.Single(r => r.Id == legacy.Id);
        saved.Status.Should().Be(DesignRequestStatus.Pending);
        queue.Verify(q => q.EnqueueAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApproveAsync_requires_AwaitingApproval_state()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var (svc, _, _, _) = MakeService(db);

        await svc.CreateAiRequestAsync(userId: 1, ipAddress: null, SampleAiDto());
        var id = db.DesignRequests.Single().Id;

        var cannotApprove = await svc.ApproveAsync(id, 1);
        cannotApprove.Success.Should().BeFalse();

        // Simulate pipeline completion
        var entity = db.DesignRequests.Single();
        entity.Status = DesignRequestStatus.AwaitingApproval;
        await db.SaveChangesAsync();

        var ok = await svc.ApproveAsync(id, 1);
        ok.Success.Should().BeTrue();
        // Customer approval moves to Approved; Final is set later by admin on delivery.
        db.DesignRequests.Single().Status.Should().Be(DesignRequestStatus.Approved);
    }

    [Fact]
    public async Task ApproveAsync_rejects_other_users()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        db.Users.Add(DbHelper.MakeUser(2, "other@example.com"));
        await db.SaveChangesAsync();
        var (svc, _, _, _) = MakeService(db);

        await svc.CreateAiRequestAsync(userId: 1, ipAddress: null, SampleAiDto());
        var id = db.DesignRequests.Single().Id;
        var entity = db.DesignRequests.Single();
        entity.Status = DesignRequestStatus.AwaitingApproval;
        await db.SaveChangesAsync();

        var result = await svc.ApproveAsync(id, 2);
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
    }
}
