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
/// Tests for POST /api/admin/design-requests/{id}/upload-preview (admin sets the
/// designer's result). Exercises <see cref="AdminDesignRequestService.UploadPreviewAsync"/>
/// directly — the DesignerPreviewPath stamp, the Order.OrderState advance guard
/// (Manual → DesignReady, AI → CustomerApproval), and the best-effort customer email.
/// </summary>
public class UploadPreviewAsyncTests
{
    private static BannerFileStorage MakeStorage() =>
        new(Options.Create(new FileStorageOptions
        {
            LocalRoot = Path.Combine(Path.GetTempPath(), "bs-upload-preview-" + Guid.NewGuid().ToString("N")),
            PublicBaseUrl = "/files"
        }));

    private static AdminDesignRequestService MakeService(
        BannerShopDbContext db, Mock<IEmailService>? email = null)
    {
        email ??= new Mock<IEmailService>();
        var storage = MakeStorage();
        var stripe = new Mock<IStripePaymentService>().Object;
        var queue = new Mock<IDesignRequestJobQueue>().Object;
        var images = new Mock<IImageProcessingService>().Object;
        var credits = new Mock<IAiCreditService>().Object;
        var pricing = new BannerShop.Api.Services.PricingService(db);
        var baseSvc = new DesignRequestService(
            db, stripe, queue, MakeStorage(), images, email.Object, credits, pricing,
            NullLogger<DesignRequestService>.Instance);

        return new AdminDesignRequestService(
            db, storage, email.Object, baseSvc, NullLogger<AdminDesignRequestService>.Instance);
    }

    private static async Task SeedAsync(BannerShopDbContext db)
    {
        db.Users.Add(DbHelper.MakeUser(1, email: "customer@shop.no"));
        db.BannerTemplates.Add(new BannerTemplate
        {
            Id = 1, Category = BannerTemplateCategory.Birthday,
            NameNb = "Bursdag", NameEn = "Birthday", SortOrder = 10
        });
        await db.SaveChangesAsync();
    }

    private static DesignRequest MakeRequest(
        DesignRequestMode mode, Order? order = null, int? userId = 1)
        => new DesignRequest
        {
            UserId = userId,
            BannerTemplateId = 1,
            Mode = mode,
            Language = "nb",
            PersonName = "Ola",
            TextContent = "Gratulerer",
            ThemeDescription = "tropisk",
            AspectRatio = "16:9",
            Status = DesignRequestStatus.Pending,
            Order = order,
            OrderId = order?.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    private static Order MakeOrder(OrderType type, OrderState state)
        => new Order
        {
            UserId = 1,
            OrderType = type,
            OrderState = state,
            Status = OrderStatus.Paid,
            TotalNok = 495m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    [Fact]
    public async Task NotFound_ReturnsFail()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var svc = MakeService(db);

        var result = await svc.UploadPreviewAsync(999, "design-previews/x.png");

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Design request not found.");
    }

    [Fact]
    public async Task SetsDesignerPreviewPathAndStatus()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var r = MakeRequest(DesignRequestMode.Manual);
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        var result = await svc.UploadPreviewAsync(r.Id, "design-previews/abc.png");

        result.Success.Should().BeTrue();
        var reloaded = await db.DesignRequests.FindAsync(r.Id);
        reloaded!.DesignerPreviewPath.Should().Be("design-previews/abc.png");
        reloaded.Status.Should().Be(DesignRequestStatus.AwaitingApproval);
    }

    [Fact]
    public async Task ManualMode_LinkedOrder_AdvancesToDesignReady()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var order = MakeOrder(OrderType.ManualDesign, OrderState.Paid);
        db.Orders.Add(order);
        var r = MakeRequest(DesignRequestMode.Manual, order);
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        var result = await svc.UploadPreviewAsync(r.Id, "design-previews/abc.png");

        result.Success.Should().BeTrue();
        var reloadedOrder = await db.Orders.FindAsync(order.Id);
        reloadedOrder!.OrderState.Should().Be(OrderState.DesignReady);
    }

    [Fact]
    public async Task AiMode_LinkedOrder_AdvancesToCustomerApproval()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var order = MakeOrder(OrderType.AiBanner, OrderState.Paid);
        db.Orders.Add(order);
        var r = MakeRequest(DesignRequestMode.Ai, order);
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        var result = await svc.UploadPreviewAsync(r.Id, "design-previews/abc.png");

        result.Success.Should().BeTrue();
        var reloadedOrder = await db.Orders.FindAsync(order.Id);
        reloadedOrder!.OrderState.Should().Be(OrderState.CustomerApproval);
    }

    [Fact]
    public async Task InvalidTransition_LeavesOrderStateUnchanged_ButStillUpdatesRequest()
    {
        // AI mode always targets CustomerApproval, but a ManualDesign order's next step
        // from Paid is DesignReady — CustomerApproval is not the immediate successor, so
        // the guard must silently skip the order-state advance rather than throwing.
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var order = MakeOrder(OrderType.ManualDesign, OrderState.Paid);
        db.Orders.Add(order);
        var r = MakeRequest(DesignRequestMode.Ai, order);
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        var result = await svc.UploadPreviewAsync(r.Id, "design-previews/abc.png");

        result.Success.Should().BeTrue();
        var reloadedOrder = await db.Orders.FindAsync(order.Id);
        reloadedOrder!.OrderState.Should().Be(OrderState.Paid, "no valid immediate transition exists");
        var reloadedRequest = await db.DesignRequests.FindAsync(r.Id);
        reloadedRequest!.DesignerPreviewPath.Should().Be("design-previews/abc.png");
    }

    [Fact]
    public async Task NoLinkedOrder_SucceedsWithoutTouchingAnyOrder()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var r = MakeRequest(DesignRequestMode.Manual, order: null);
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var svc = MakeService(db);

        var result = await svc.UploadPreviewAsync(r.Id, "design-previews/abc.png");

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task SendsApprovalEmailToUser()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var r = MakeRequest(DesignRequestMode.Manual);
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var emailMock = new Mock<IEmailService>();
        var svc = MakeService(db, emailMock);

        await svc.UploadPreviewAsync(r.Id, "design-previews/abc.png");

        emailMock.Verify(e => e.SendAsync(
            "customer@shop.no",
            It.Is<string>(s => s.Contains("godkjenning")),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AnonymousRequest_NoUser_SkipsEmailWithoutThrowing()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var r = MakeRequest(DesignRequestMode.Manual, userId: null);
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var emailMock = new Mock<IEmailService>();
        var svc = MakeService(db, emailMock);

        var result = await svc.UploadPreviewAsync(r.Id, "design-previews/abc.png");

        result.Success.Should().BeTrue();
        emailMock.Verify(e => e.SendAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EmailSendFailure_IsSwallowed_RequestStillSucceeds()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var r = MakeRequest(DesignRequestMode.Manual);
        db.DesignRequests.Add(r);
        await db.SaveChangesAsync();
        var emailMock = new Mock<IEmailService>();
        emailMock.Setup(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));
        var svc = MakeService(db, emailMock);

        var result = await svc.UploadPreviewAsync(r.Id, "design-previews/abc.png");

        result.Success.Should().BeTrue("email failures must not fail the admin upload");
    }
}
