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

    // ── BANNERSH-136: Manual flow no longer creates a Stripe PaymentIntent upfront ──

    [Fact]
    public async Task CreateManualRequestAsync_does_not_call_stripe_and_returns_price_breakdown_when_catalog_seeded()
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
        result.ClientSecret.Should().BeNull();   // BANNERSH-136: no Stripe PI

        var saved = db.DesignRequests.Single();
        saved.PriceNok.Should().Be(495m);
        saved.BannerPriceNok.Should().Be(result.BannerPriceNok);
        saved.BannerSizeId.Should().NotBeNull();
        saved.StripePaymentIntentId.Should().BeNull(); // never set in the new flow

        // BANNERSH-136: no Stripe charge is made upfront.
        stripe.Verify(s => s.CreatePaymentIntentAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
        result.ClientSecret.Should().BeNull();   // BANNERSH-136: no Stripe PI
        db.DesignRequests.Single().BannerSizeId.Should().BeNull();

        // BANNERSH-136: no Stripe call even in degraded mode.
        stripe.Verify(s => s.CreatePaymentIntentAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── MarkPaidAndEnqueueAsync: legacy Manual mode still works via dead-code guard ──

    [Fact]
    public async Task MarkPaidAndEnqueueAsync_manual_legacy_pi_flips_to_InProgress()
    {
        // Simulate a legacy in-flight Manual request that was created BEFORE BANNERSH-136
        // and still has a StripePaymentIntentId (the new flow never sets this field, so
        // this test seeds the PI manually rather than going through CreateManualRequestAsync).
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var (svc, _, queue, _) = MakeService(db);

        var order = new Order
        {
            UserId = 1,
            OrderType = OrderType.ManualDesign,
            OrderState = OrderState.Draft,
            Status = OrderStatus.Draft,
            TotalNok = 495m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Orders.Add(order);
        var legacyRequest = new BannerShop.Core.Entities.DesignRequest
        {
            UserId = 1,
            BannerTemplateId = 1,
            Mode = DesignRequestMode.Manual,
            Language = "nb",
            PersonName = "Ola",
            TextContent = "Hi",
            ThemeDescription = "x",
            AspectRatio = "16:9",
            Status = DesignRequestStatus.Pending,
            PriceNok = 495m,
            StripePaymentIntentId = "pi_test_123",
            RegenerationsRemaining = 0,
            Order = order,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.DesignRequests.Add(legacyRequest);
        await db.SaveChangesAsync();

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

    // ── ApproveAsync: selectedHeightCm + linked Order state advancement ───────

    [Fact]
    public async Task ApproveAsync_WithSelectedHeight_UpdatesAspectRatioAndAdvancesOrder()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var (svc, _, _, _) = MakeService(db);

        // Create a linked Order in CustomerApproval state
        var order = new Order
        {
            UserId      = 1,
            OrderType   = OrderType.ManualDesign,
            OrderState  = OrderState.CustomerApproval,
            Status      = OrderStatus.Paid,
            TotalNok    = 495m,
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync(); // order.Id is now set

        var request = new DesignRequest
        {
            Id               = 60,
            UserId           = 1,
            BannerTemplateId = 1,
            Mode             = DesignRequestMode.Manual,
            Language         = "nb",
            PersonName       = "Ola",
            TextContent      = "Hi",
            ThemeDescription = "x",
            AspectRatio      = "300x150",
            Status           = DesignRequestStatus.AwaitingApproval,
            PriceNok         = 495m,
            RevisionCount    = 0,
            OrderId          = order.Id,
            CreatedAt        = DateTime.UtcNow,
            UpdatedAt        = DateTime.UtcNow
        };
        db.DesignRequests.Add(request);
        await db.SaveChangesAsync();

        var result = await svc.ApproveAsync(60, callerUserId: 1, selectedHeightCm: 200);

        result.Success.Should().BeTrue();
        var savedDr = db.DesignRequests.Single(r => r.Id == 60);
        savedDr.Status.Should().Be(DesignRequestStatus.Approved);
        // selectedHeightCm=200 rewrites AspectRatio to "300x200" (width preserved, height updated)
        savedDr.AspectRatio.Should().Contain("200");

        // Linked order should advance: CustomerApproval → InProduction
        var savedOrder = db.Orders.Single(o => o.Id == order.Id);
        savedOrder.OrderState.Should().Be(OrderState.InProduction);
    }

    // ── ListMineAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ListMineAsync_WithExistingRequest_ReturnsItemsWithPreviewUrl()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        db.DesignRequests.Add(new DesignRequest
        {
            UserId           = 1,
            BannerTemplateId = 1,
            Mode             = DesignRequestMode.Ai,
            Language         = "nb",
            PersonName       = "Ola",
            TextContent      = "Gratulerer",
            ThemeDescription = "tropisk",
            AspectRatio      = "16:9",
            Status           = DesignRequestStatus.AwaitingApproval,
            PriceNok         = 0m,
            AiPreviewPath    = "design-requests/1/preview.jpg",
            CreatedAt        = DateTime.UtcNow,
            UpdatedAt        = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var (svc, _, _, _) = MakeService(db);

        var items = await svc.ListMineAsync(1);

        items.Should().HaveCount(1);
        items[0].PersonName.Should().Be("Ola");
        items[0].PreviewUrl.Should().NotBeNull(); // AiPreviewPath drives the preview URL
    }

    // ── RequestRevisionAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task RequestRevisionAsync_WrongUser_ReturnsForbiddenError()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        db.Users.Add(DbHelper.MakeUser(2, "other@example.com"));
        db.DesignRequests.Add(new DesignRequest
        {
            Id               = 50,
            UserId           = 1,
            BannerTemplateId = 1,
            Mode             = DesignRequestMode.Manual,
            Language         = "nb",
            PersonName       = "Ola",
            TextContent      = "Hi",
            ThemeDescription = "x",
            AspectRatio      = "16:9",
            Status           = DesignRequestStatus.AwaitingApproval,
            PriceNok         = 495m,
            RevisionCount    = 0,
            CreatedAt        = DateTime.UtcNow,
            UpdatedAt        = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var (svc, _, _, _) = MakeService(db);

        var result = await svc.RequestRevisionAsync(50, callerUserId: 2, comment: "wrong user");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
    }

    [Fact]
    public async Task RequestRevisionAsync_WrongStatus_ReturnsError()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        db.DesignRequests.Add(new DesignRequest
        {
            Id               = 51,
            UserId           = 1,
            BannerTemplateId = 1,
            Mode             = DesignRequestMode.Manual,
            Language         = "nb",
            PersonName       = "Ola",
            TextContent      = "Hi",
            ThemeDescription = "x",
            AspectRatio      = "16:9",
            Status           = DesignRequestStatus.InProgress, // not AwaitingApproval
            PriceNok         = 495m,
            RevisionCount    = 0,
            CreatedAt        = DateTime.UtcNow,
            UpdatedAt        = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var (svc, _, _, _) = MakeService(db);

        var result = await svc.RequestRevisionAsync(51, callerUserId: 1, comment: "change it");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Cannot request");
    }

    [Fact]
    public async Task RequestRevisionAsync_AiMode_ReturnsRevisionNotAvailableError()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        db.DesignRequests.Add(new DesignRequest
        {
            Id               = 52,
            UserId           = 1,
            BannerTemplateId = 1,
            Mode             = DesignRequestMode.Ai, // not Manual
            Language         = "nb",
            PersonName       = "Ola",
            TextContent      = "Hi",
            ThemeDescription = "x",
            AspectRatio      = "16:9",
            Status           = DesignRequestStatus.AwaitingApproval,
            PriceNok         = 0m,
            RevisionCount    = 0,
            CreatedAt        = DateTime.UtcNow,
            UpdatedAt        = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var (svc, _, _, _) = MakeService(db);

        var result = await svc.RequestRevisionAsync(52, callerUserId: 1, comment: "change it");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("manual");
    }

    [Fact]
    public async Task RequestRevisionAsync_MaxRevisionsReached_ReturnsError()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        db.DesignRequests.Add(new DesignRequest
        {
            Id               = 53,
            UserId           = 1,
            BannerTemplateId = 1,
            Mode             = DesignRequestMode.Manual,
            Language         = "nb",
            PersonName       = "Ola",
            TextContent      = "Hi",
            ThemeDescription = "x",
            AspectRatio      = "16:9",
            Status           = DesignRequestStatus.AwaitingApproval,
            PriceNok         = 495m,
            RevisionCount    = 1, // already used revision
            CreatedAt        = DateTime.UtcNow,
            UpdatedAt        = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var (svc, _, _, _) = MakeService(db);

        var result = await svc.RequestRevisionAsync(53, callerUserId: 1, comment: "another change");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("revision");
    }

    [Fact]
    public async Task RequestRevisionAsync_ValidRequest_AddsRevisionAndChangesStatus()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        db.DesignRequests.Add(new DesignRequest
        {
            Id               = 54,
            UserId           = 1,
            BannerTemplateId = 1,
            Mode             = DesignRequestMode.Manual,
            Language         = "nb",
            PersonName       = "Ola",
            TextContent      = "Hi",
            ThemeDescription = "x",
            AspectRatio      = "16:9",
            Status           = DesignRequestStatus.AwaitingApproval,
            PriceNok         = 495m,
            RevisionCount    = 0,
            CreatedAt        = DateTime.UtcNow,
            UpdatedAt        = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var (svc, _, _, _) = MakeService(db);

        var result = await svc.RequestRevisionAsync(54, callerUserId: 1, comment: "Please change the font");

        result.Success.Should().BeTrue();
        result.Detail.Should().NotBeNull();
        result.Detail!.Revisions.Should().HaveCount(1);
        result.Detail.Revisions[0].CustomerComment.Should().Be("Please change the font");

        var saved = db.DesignRequests.Single(r => r.Id == 54);
        saved.Status.Should().Be(DesignRequestStatus.RevisionRequested);
        saved.RevisionCount.Should().Be(1);
    }

    // ── ParseDimensions: WxH format and fallback ─────────────────────────────

    [Fact]
    public async Task CreateManualRequestAsync_WxHAspectRatio_ParsesDimensionsCorrectly()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        DbHelper.SeedPricingParameters(db);
        DbHelper.SeedCatalog(db);
        var (svc, _, _, _) = MakeService(db);

        var result = await svc.CreateManualRequestAsync(1, new CreateManualDesignRequestDto
        {
            TemplateId       = 1,
            Language         = "nb",
            PersonName       = "Ola",
            TextContent      = "Hi",
            ThemeDescription = "x",
            AspectRatio      = "300x150" // WxH format → ParseDimensions WxH branch
        });

        result.Success.Should().BeTrue();
        db.DesignRequests.Single().AspectRatio.Should().Be("300x150");
    }

    [Fact]
    public async Task CreateManualRequestAsync_UnknownAspectRatio_FallsBackToDefault()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        // No catalog seeded — degrade to design-fee-only
        var (svc, _, _, _) = MakeService(db);

        var result = await svc.CreateManualRequestAsync(1, new CreateManualDesignRequestDto
        {
            TemplateId       = 1,
            Language         = "nb",
            PersonName       = "Ola",
            TextContent      = "Hi",
            ThemeDescription = "x",
            AspectRatio      = "4:3" // unknown format → ParseDimensions returns (250, 150)
        });

        result.Success.Should().BeTrue();
        result.DesignPriceNok.Should().Be(495m);
        result.BannerPriceNok.Should().Be(0m); // no matching catalog entry → degrade
    }
}
