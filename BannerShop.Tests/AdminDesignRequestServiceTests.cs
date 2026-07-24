using BannerShop.Api.Models.DesignRequests;
using BannerShop.Api.Services.AiCredits;
using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Api.Services.DesignRequests;
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
/// Unit tests for <see cref="AdminDesignRequestService.ListAsync"/> — the admin AI
/// generations gallery / abuse-monitoring list (BANNERSH-286). Exercises the
/// status/mode/date/search filters, paging clamps, and the projection logic that
/// exposes anonymous IPs, formats customer names, and resolves the preview URL.
/// </summary>
public class AdminDesignRequestServiceTests
{
    private const string PublicBase = "/files";

    private static BannerFileStorage MakeStorage() =>
        new(Options.Create(new FileStorageOptions
        {
            LocalRoot = Path.Combine(Path.GetTempPath(), "bs-tests-" + Guid.NewGuid().ToString("N")),
            PublicBaseUrl = PublicBase
        }));

    private static AdminDesignRequestService MakeService(BannerShopDbContext db)
    {
        var storage = MakeStorage();
        var email = new Mock<IEmailService>().Object;

        // ListAsync only touches _db and _storage, but the ctor requires a base
        // DesignRequestService. Build a throwaway one with mocked collaborators.
        var stripe = new Mock<IStripePaymentService>().Object;
        var queue = new Mock<IDesignRequestJobQueue>().Object;
        var images = new Mock<IImageProcessingService>().Object;
        var credits = new Mock<IAiCreditService>().Object;
        var pricing = new BannerShop.Api.Services.PricingService(db);
        var baseSvc = new DesignRequestService(
            db, stripe, queue, MakeStorage(), images, email, credits, pricing,
            NullLogger<DesignRequestService>.Instance);

        return new AdminDesignRequestService(
            db, storage, email, baseSvc, NullLogger<AdminDesignRequestService>.Instance);
    }

    private static DesignRequest MakeRequest(
        int id,
        DesignRequestMode mode = DesignRequestMode.Ai,
        DesignRequestStatus status = DesignRequestStatus.AwaitingApproval,
        int? userId = null,
        string? ipAddress = null,
        string personName = "Ola",
        string theme = "tropisk",
        DateTime? createdAt = null)
        => new DesignRequest
        {
            Id = id,
            Mode = mode,
            Status = status,
            UserId = userId,
            IpAddress = ipAddress,
            BannerTemplateId = 1,
            PersonName = personName,
            ThemeDescription = theme,
            AspectRatio = "16:9",
            CreatedAt = createdAt ?? new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            UpdatedAt = createdAt ?? new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
        };

    // -- Empty / basics ---------------------------------------------------------

    [Fact]
    public async Task ListAsync_EmptyDb_ReturnsEmpty()
    {
        var db = DbHelper.CreateInMemory();
        var svc = MakeService(db);

        var result = await svc.ListAsync(new AdminDesignRequestFilter());

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.Page.Should().Be(1);
    }

    [Fact]
    public async Task ListAsync_OrdersByCreatedAtDescending()
    {
        var db = DbHelper.CreateInMemory();
        db.DesignRequests.AddRange(
            MakeRequest(1, createdAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            MakeRequest(2, createdAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)),
            MakeRequest(3, createdAt: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        var result = await svc.ListAsync(new AdminDesignRequestFilter());

        result.Items.Select(i => i.Id).Should().ContainInOrder(2, 3, 1);
        result.TotalCount.Should().Be(3);
    }

    // -- Filters ----------------------------------------------------------------

    [Fact]
    public async Task ListAsync_StatusFilter_IsCaseInsensitive()
    {
        var db = DbHelper.CreateInMemory();
        db.DesignRequests.AddRange(
            MakeRequest(1, status: DesignRequestStatus.Failed),
            MakeRequest(2, status: DesignRequestStatus.AwaitingApproval));
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        var result = await svc.ListAsync(new AdminDesignRequestFilter { Status = "failed" });

        result.Items.Should().ContainSingle().Which.Id.Should().Be(1);
    }

    [Fact]
    public async Task ListAsync_InvalidStatus_IsIgnored()
    {
        var db = DbHelper.CreateInMemory();
        db.DesignRequests.AddRange(MakeRequest(1), MakeRequest(2));
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        var result = await svc.ListAsync(new AdminDesignRequestFilter { Status = "not-a-status" });

        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task ListAsync_ModeFilter_Filters()
    {
        var db = DbHelper.CreateInMemory();
        db.DesignRequests.AddRange(
            MakeRequest(1, mode: DesignRequestMode.Ai),
            MakeRequest(2, mode: DesignRequestMode.Manual));
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        var result = await svc.ListAsync(new AdminDesignRequestFilter { Mode = "Manual" });

        result.Items.Should().ContainSingle().Which.Id.Should().Be(2);
    }

    [Fact]
    public async Task ListAsync_DateRange_Filters()
    {
        var db = DbHelper.CreateInMemory();
        db.DesignRequests.AddRange(
            MakeRequest(1, createdAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            MakeRequest(2, createdAt: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)),
            MakeRequest(3, createdAt: new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        var result = await svc.ListAsync(new AdminDesignRequestFilter
        {
            FromUtc = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        });

        result.Items.Should().ContainSingle().Which.Id.Should().Be(2);
    }

    [Fact]
    public async Task ListAsync_Search_MatchesPersonNameCaseInsensitive()
    {
        var db = DbHelper.CreateInMemory();
        db.DesignRequests.AddRange(
            MakeRequest(1, personName: "Kari Nordmann"),
            MakeRequest(2, personName: "Ola Hansen"));
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        var result = await svc.ListAsync(new AdminDesignRequestFilter { Search = "kari" });

        result.Items.Should().ContainSingle().Which.Id.Should().Be(1);
    }

    [Fact]
    public async Task ListAsync_Search_MatchesUserNameAndEmail()
    {
        var db = DbHelper.CreateInMemory();
        db.Users.Add(DbHelper.MakeUser(10, email: "buyer@shop.no"));
        db.Users.Add(DbHelper.MakeUser(11, email: "other@shop.no"));
        db.DesignRequests.AddRange(
            MakeRequest(1, userId: 10, personName: "zzz"),
            MakeRequest(2, userId: 11, personName: "yyy"));
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        var byEmail = await svc.ListAsync(new AdminDesignRequestFilter { Search = "buyer@shop" });
        byEmail.Items.Should().ContainSingle().Which.Id.Should().Be(1);

        // "Test User" is the name given by DbHelper.MakeUser -> matches both rows.
        var byName = await svc.ListAsync(new AdminDesignRequestFilter { Search = "test user" });
        byName.TotalCount.Should().Be(2);
    }

    // -- Paging clamps ----------------------------------------------------------

    [Fact]
    public async Task ListAsync_ClampsPageSizeAndPage()
    {
        var db = DbHelper.CreateInMemory();
        for (var i = 1; i <= 5; i++) db.DesignRequests.Add(MakeRequest(i));
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        var result = await svc.ListAsync(new AdminDesignRequestFilter { Page = 0, PageSize = 500 });

        result.Page.Should().Be(1, "page < 1 is clamped up to 1");
        result.PageSize.Should().Be(100, "page size > 100 is clamped down to 100");
        result.Items.Should().HaveCount(5);
        result.TotalCount.Should().Be(5);
    }

    [Fact]
    public async Task ListAsync_Paging_SkipsAndTakes()
    {
        var db = DbHelper.CreateInMemory();
        // 5 rows with strictly increasing CreatedAt so ordering is deterministic.
        for (var i = 1; i <= 5; i++)
            db.DesignRequests.Add(MakeRequest(i, createdAt: new DateTime(2026, 1, i, 0, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        var page2 = await svc.ListAsync(new AdminDesignRequestFilter { Page = 2, PageSize = 2 });

        // Desc order: 5,4,3,2,1 -> page 2 (size 2) = 3,2
        page2.Items.Select(i => i.Id).Should().ContainInOrder(3, 2);
        page2.TotalCount.Should().Be(5, "total reflects all matches, not just the page");
    }

    // -- Projection: anon IP, customer name, preview URL ------------------------

    [Fact]
    public async Task ListAsync_AnonymousRequest_ExposesIpAndFormatsCustomerName()
    {
        var db = DbHelper.CreateInMemory();
        db.DesignRequests.Add(MakeRequest(1, userId: null, ipAddress: "203.0.113.7"));
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        var item = (await svc.ListAsync(new AdminDesignRequestFilter())).Items.Single();

        item.IpAddress.Should().Be("203.0.113.7");
        item.UserId.Should().Be(0);
        item.CustomerName.Should().Be("anon (203.0.113.7)");
        item.CustomerEmail.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_AnonymousWithoutIp_CustomerNameIsAnon()
    {
        var db = DbHelper.CreateInMemory();
        db.DesignRequests.Add(MakeRequest(1, userId: null, ipAddress: null));
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        var item = (await svc.ListAsync(new AdminDesignRequestFilter())).Items.Single();

        item.IpAddress.Should().BeNull();
        item.CustomerName.Should().Be("anon");
    }

    [Fact]
    public async Task ListAsync_AuthenticatedRequest_HidesIpAndUsesUserIdentity()
    {
        var db = DbHelper.CreateInMemory();
        db.Users.Add(DbHelper.MakeUser(42, email: "buyer@shop.no"));
        // Even if a stray IP is stored, an authenticated request must not leak it.
        db.DesignRequests.Add(MakeRequest(1, userId: 42, ipAddress: "203.0.113.9"));
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        var item = (await svc.ListAsync(new AdminDesignRequestFilter())).Items.Single();

        item.IpAddress.Should().BeNull("authenticated requests never expose the stored IP");
        item.UserId.Should().Be(42);
        item.CustomerName.Should().Be("Test User");
        item.CustomerEmail.Should().Be("buyer@shop.no");
    }

    [Fact]
    public async Task ListAsync_PreviewUrl_PrefersAiPreviewPath()
    {
        var db = DbHelper.CreateInMemory();
        var r = MakeRequest(1);
        r.AiPreviewPath = "ai/preview.png";
        r.DesignerPreviewPath = "designer/preview.png";
        r.FinalCroppedStoragePath = "final/cropped.png";
        r.AiResultStoragePath = "ai/result.png";
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        var item = (await svc.ListAsync(new AdminDesignRequestFilter())).Items.Single();

        item.PreviewUrl.Should().Be($"{PublicBase}/ai/preview.png");
    }

    [Fact]
    public async Task ListAsync_PreviewUrl_FallsBackThroughChain()
    {
        var db = DbHelper.CreateInMemory();
        var r = MakeRequest(1);
        // No AiPreviewPath / DesignerPreviewPath -> next in chain is FinalCropped.
        r.FinalCroppedStoragePath = "final/cropped.png";
        r.AiResultStoragePath = "ai/result.png";
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        var item = (await svc.ListAsync(new AdminDesignRequestFilter())).Items.Single();

        item.PreviewUrl.Should().Be($"{PublicBase}/final/cropped.png");
    }

    [Fact]
    public async Task ListAsync_PreviewUrl_NullWhenNoPathsSet()
    {
        var db = DbHelper.CreateInMemory();
        db.DesignRequests.Add(MakeRequest(1));
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        var item = (await svc.ListAsync(new AdminDesignRequestFilter())).Items.Single();

        item.PreviewUrl.Should().BeNull();
    }
}
