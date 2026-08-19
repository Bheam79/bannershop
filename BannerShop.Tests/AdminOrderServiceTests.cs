using BannerShop.Api.Models.Orders;
using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Api.Services.Email;
using BannerShop.Api.Services.Orders;
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
/// Unit tests for <see cref="AdminOrderService"/> that exercise logic in isolation,
/// without the HTTP stack, so the Stripe-throw path can be covered with a mock that
/// actually throws (unlike <see cref="MockStripePaymentService"/> which always succeeds).
/// </summary>
public class AdminOrderServiceTests
{
    // ── Setup helpers ────────────────────────────────────────────────────────

    private static AdminOrderService CreateService(
        BannerShop.Infrastructure.Data.BannerShopDbContext db,
        IStripePaymentService? stripe = null,
        Mock<IEmailService>? emailMock = null,
        Mock<IOrderService>? ordersMock = null)
    {
        emailMock ??= new Mock<IEmailService>();
        emailMock.Setup(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        stripe ??= new Mock<IStripePaymentService>().Object;
        ordersMock ??= new Mock<IOrderService>();

        var storage = new BannerFileStorage(Options.Create(new FileStorageOptions()));

        return new AdminOrderService(db, emailMock.Object, storage, stripe,
                                     ordersMock.Object, NullLogger<AdminOrderService>.Instance);
    }

    // ── CapturePaymentAsync ──────────────────────────────────────────────────

    /// <summary>
    /// BANNERSH-269 / BANNERSH-270: When the IStripePaymentService throws, the service must
    /// return a FailTransition result (ErrorType=InvalidTransition) rather than a NotFound
    /// result. This ensures the controller maps the error to 422 UnprocessableEntity instead
    /// of 404 Not Found.
    /// </summary>
    [Fact]
    public async Task CapturePaymentAsync_WhenStripeThrows_ReturnsFailTransitionResult()
    {
        // Arrange
        var db = DbHelper.CreateInMemory();
        DbHelper.SeedPricingParameters(db);
        DbHelper.SeedCatalog(db);

        // Insert an order in Paid state with a PI id so it passes all guard checks
        var order = new Order
        {
            UserId = 1,
            Status = OrderStatus.Paid,
            OrderState = OrderState.Paid,
            StripePaymentIntentId = "pi_test_throws",
            TotalNok = 810m,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        // Stripe mock that always throws
        var stripeMock = new Mock<IStripePaymentService>();
        stripeMock
            .Setup(s => s.CapturePaymentIntentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Stripe is down"));

        var service = CreateService(db, stripeMock.Object);

        // Act
        var result = await service.CapturePaymentAsync(order.Id);

        // Assert — must be a failure with InvalidTransition error type (→ 422 at the controller)
        result.Success.Should().BeFalse();
        result.ErrorType.Should().Be(OrderActionErrorType.InvalidTransition);
        result.Error.Should().Contain("Stripe capture failed");
    }

    [Fact]
    public async Task CapturePaymentAsync_WhenOrderNotFound_ReturnsNotFoundResult()
    {
        var db = DbHelper.CreateInMemory();
        var service = CreateService(db);

        var result = await service.CapturePaymentAsync(99999);

        result.Success.Should().BeFalse();
        result.ErrorType.Should().Be(OrderActionErrorType.NotFound);
    }

    [Fact]
    public async Task CapturePaymentAsync_WhenNoPaymentIntent_ReturnsFailTransitionResult()
    {
        // Arrange: order in Paid state but no StripePaymentIntentId
        var db = DbHelper.CreateInMemory();
        var order = new Order
        {
            UserId = 1,
            Status = OrderStatus.Paid,
            OrderState = OrderState.Paid,
            StripePaymentIntentId = null,
            TotalNok = 500m,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var service = CreateService(db);

        // Act
        var result = await service.CapturePaymentAsync(order.Id);

        // Assert — must be InvalidTransition (→ 422), not NotFound (→ 404)
        result.Success.Should().BeFalse();
        result.ErrorType.Should().Be(OrderActionErrorType.InvalidTransition);
        result.Error.Should().Contain("no Stripe PaymentIntent");
    }

    [Fact]
    public async Task CapturePaymentAsync_WhenOrderInNonCapturableState_ReturnsFailTransitionResult()
    {
        // Arrange: order in Shipped state (not capturable)
        var db = DbHelper.CreateInMemory();
        var order = new Order
        {
            UserId = 1,
            Status = OrderStatus.Shipped,
            OrderState = OrderState.Shipped,
            StripePaymentIntentId = "pi_test_shipped",
            TotalNok = 500m,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var result = await service.CapturePaymentAsync(order.Id);

        result.Success.Should().BeFalse();
        result.ErrorType.Should().Be(OrderActionErrorType.InvalidTransition);
    }

    [Fact]
    public async Task CapturePaymentAsync_PaidOrderWithValidPI_ReturnsSuccess()
    {
        // Arrange: order in Paid state with a PI that the mock stripe accepts.
        // A User row is required because LoadFullOrderAsync does .Include(o => o.User)
        // and ToDetailDto accesses o.User?.Name — without a matching User in InMemory
        // the navigation stays null which does not cause an NPE, but we seed one anyway
        // so the loaded entity is fully consistent with production data.
        var db = DbHelper.CreateInMemory();
        DbHelper.SeedPricingParameters(db);
        DbHelper.SeedCatalog(db);

        var user = DbHelper.MakeUser(id: 1);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var order = new Order
        {
            UserId = user.Id,
            Status = OrderStatus.Paid,
            OrderState = OrderState.Paid,
            StripePaymentIntentId = "pi_test_ok",
            TotalNok = 810m,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var stripeMock = new Mock<IStripePaymentService>();
        stripeMock
            .Setup(s => s.CapturePaymentIntentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService(db, stripeMock.Object);

        // Act
        var result = await service.CapturePaymentAsync(order.Id);

        // Assert
        result.Success.Should().BeTrue();
        stripeMock.Verify(
            s => s.CapturePaymentIntentAsync("pi_test_ok", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── MarkPaidManuallyAsync ────────────────────────────────────────────────
    // Admin "mark as paid" action used when the Stripe webhook was never delivered.

    [Fact]
    public async Task MarkPaidManuallyAsync_OrderNotFound_ReturnsNotFoundResult()
    {
        var db = DbHelper.CreateInMemory();
        var service = CreateService(db);

        var result = await service.MarkPaidManuallyAsync(99999);

        result.Success.Should().BeFalse();
        result.ErrorType.Should().Be(OrderActionErrorType.NotFound);
    }

    [Fact]
    public async Task MarkPaidManuallyAsync_OrderAlreadyPaid_ReturnsFailTransitionResult()
    {
        var db = DbHelper.CreateInMemory();
        var order = new Order
        {
            UserId = 1,
            Status = OrderStatus.Paid,
            OrderState = OrderState.Paid,
            StripePaymentIntentId = "pi_already_paid",
            TotalNok = 500m,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var service = CreateService(db);

        var result = await service.MarkPaidManuallyAsync(order.Id);

        result.Success.Should().BeFalse();
        result.ErrorType.Should().Be(OrderActionErrorType.InvalidTransition);
        result.Error.Should().Contain("only Draft or PendingPayment");
    }

    [Fact]
    public async Task MarkPaidManuallyAsync_WithStripePaymentIntent_DelegatesToOrderServiceMarkPaidAsync()
    {
        // Arrange: PendingPayment order with a PI — the normal case, since
        // OrderService.CreateDraftAsync always mints a PaymentIntent up front.
        var db = DbHelper.CreateInMemory();
        DbHelper.SeedPricingParameters(db);
        DbHelper.SeedCatalog(db);

        var user = DbHelper.MakeUser(id: 1);
        db.Users.Add(user);
        var order = new Order
        {
            UserId = user.Id,
            Status = OrderStatus.PendingPayment,
            OrderState = OrderState.Draft,
            StripePaymentIntentId = "pi_needs_manual_mark",
            TotalNok = 810m,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var ordersMock = new Mock<IOrderService>();
        ordersMock
            .Setup(o => o.MarkPaidAsync("pi_needs_manual_mark", order.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var service = CreateService(db, ordersMock: ordersMock);

        // Act
        var result = await service.MarkPaidManuallyAsync(order.Id);

        // Assert — delegates to the shared IOrderService.MarkPaidAsync (production seeding,
        // confirmation email, AI credits) rather than flipping the status directly.
        result.Success.Should().BeTrue();
        ordersMock.Verify(
            o => o.MarkPaidAsync("pi_needs_manual_mark", order.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MarkPaidManuallyAsync_WithoutStripePaymentIntent_MarksPaidDirectlyWithoutDelegating()
    {
        // Arrange: PendingPayment order with NO PaymentIntent (e.g. legacy/imported data
        // created outside the normal Stripe-backed draft flow).
        var db = DbHelper.CreateInMemory();
        DbHelper.SeedPricingParameters(db);
        DbHelper.SeedCatalog(db);

        var user = DbHelper.MakeUser(id: 1);
        db.Users.Add(user);
        var order = new Order
        {
            UserId = user.Id,
            Status = OrderStatus.PendingPayment,
            OrderState = OrderState.Draft,
            StripePaymentIntentId = null,
            TotalNok = 810m,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var ordersMock = new Mock<IOrderService>();
        var service = CreateService(db, ordersMock: ordersMock);

        // Act
        var result = await service.MarkPaidManuallyAsync(order.Id);

        // Assert — flips the order to Paid directly, never touching IOrderService.
        result.Success.Should().BeTrue();
        result.Order!.Status.Should().Be(OrderStatus.Paid.ToString());
        ordersMock.Verify(
            o => o.MarkPaidAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);

        var reloaded = await db.Orders.FindAsync(order.Id);
        reloaded!.Status.Should().Be(OrderStatus.Paid);
        reloaded.OrderState.Should().Be(OrderState.Paid);
    }

    // ── Fire-and-forget transactional emails (BANNERSH-166) ────────────────────
    // Failures must never propagate to the caller, and a missing recipient email
    // must skip the send entirely rather than calling IEmailService with an
    // empty "to" address.

    [Fact]
    public async Task SetShippingAsync_UserHasNoEmail_SkipsSendButStillSucceeds()
    {
        var db = DbHelper.CreateInMemory();
        DbHelper.SeedPricingParameters(db);
        DbHelper.SeedCatalog(db);

        var user = DbHelper.MakeUser(id: 1, email: "");
        db.Users.Add(user);
        var order = new Order
        {
            UserId = user.Id,
            Status = OrderStatus.InProduction,
            OrderState = OrderState.InProduction,
            TotalNok = 500m,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var emailMock = new Mock<IEmailService>();
        var service = CreateService(db, emailMock: emailMock);

        var result = await service.SetShippingAsync(order.Id,
            new SetShippingRequest { Carrier = "Bring", TrackingNumber = "TRK123" });

        result.Success.Should().BeTrue();
        emailMock.Verify(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SetShippingAsync_EmailSendThrows_IsSwallowedAndStillSucceeds()
    {
        var db = DbHelper.CreateInMemory();
        DbHelper.SeedPricingParameters(db);
        DbHelper.SeedCatalog(db);

        var user = DbHelper.MakeUser(id: 1, email: "customer@example.com");
        db.Users.Add(user);
        var order = new Order
        {
            UserId = user.Id,
            Status = OrderStatus.InProduction,
            OrderState = OrderState.InProduction,
            TotalNok = 500m,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var emailMock = new Mock<IEmailService>();
        emailMock.Setup(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("SMTP down"));
        var service = CreateService(db, emailMock: emailMock);

        var result = await service.SetShippingAsync(order.Id,
            new SetShippingRequest { Carrier = "Bring", TrackingNumber = "TRK123" });

        result.Success.Should().BeTrue();
        result.Order!.Status.Should().Be(OrderStatus.Shipped.ToString());
        emailMock.Verify(e => e.SendAsync(
                "customer@example.com", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SetShippingAsync_UserHasEmail_SendsShipmentDispatchedEmail()
    {
        var db = DbHelper.CreateInMemory();
        DbHelper.SeedPricingParameters(db);
        DbHelper.SeedCatalog(db);

        var user = DbHelper.MakeUser(id: 1, email: "customer@example.com");
        db.Users.Add(user);
        var order = new Order
        {
            UserId = user.Id,
            Status = OrderStatus.InProduction,
            OrderState = OrderState.InProduction,
            TotalNok = 500m,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var emailMock = new Mock<IEmailService>();
        var service = CreateService(db, emailMock: emailMock);

        var result = await service.SetShippingAsync(order.Id,
            new SetShippingRequest { Carrier = "Bring", TrackingNumber = "TRK123" });

        result.Success.Should().BeTrue();
        emailMock.Verify(e => e.SendAsync(
                "customer@example.com",
                It.Is<string>(s => s.Contains("Bestillingen din er sendt")),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AdvanceStateAsync_ToInProduction_UserHasNoEmail_SkipsSendButStillSucceeds()
    {
        var db = DbHelper.CreateInMemory();
        var user = DbHelper.MakeUser(id: 1, email: "");
        db.Users.Add(user);
        var order = new Order
        {
            UserId = user.Id,
            Status = OrderStatus.Paid,
            OrderState = OrderState.Paid,
            TotalNok = 500m,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var emailMock = new Mock<IEmailService>();
        var service = CreateService(db, emailMock: emailMock);

        var result = await service.AdvanceStateAsync(order.Id, OrderState.InProduction);

        result.Success.Should().BeTrue();
        emailMock.Verify(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AdvanceStateAsync_ToInProduction_EmailSendThrows_IsSwallowedAndStillSucceeds()
    {
        var db = DbHelper.CreateInMemory();
        var user = DbHelper.MakeUser(id: 1, email: "customer@example.com");
        db.Users.Add(user);
        var order = new Order
        {
            UserId = user.Id,
            Status = OrderStatus.Paid,
            OrderState = OrderState.Paid,
            TotalNok = 500m,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var emailMock = new Mock<IEmailService>();
        emailMock.Setup(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("SMTP down"));
        var service = CreateService(db, emailMock: emailMock);

        var result = await service.AdvanceStateAsync(order.Id, OrderState.InProduction);

        result.Success.Should().BeTrue();
        emailMock.Verify(e => e.SendAsync(
                "customer@example.com", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AdvanceStateAsync_NotToInProduction_NeverSendsEmail()
    {
        var db = DbHelper.CreateInMemory();
        var user = DbHelper.MakeUser(id: 1, email: "customer@example.com");
        db.Users.Add(user);
        var order = new Order
        {
            UserId = user.Id,
            Status = OrderStatus.Draft,
            OrderState = OrderState.Draft,
            TotalNok = 500m,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var emailMock = new Mock<IEmailService>();
        var service = CreateService(db, emailMock: emailMock);

        var result = await service.AdvanceStateAsync(order.Id, OrderState.Paid);

        result.Success.Should().BeTrue();
        emailMock.Verify(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CancelPaymentAsync_UserHasNoEmail_SkipsSendButStillSucceeds()
    {
        var db = DbHelper.CreateInMemory();
        var user = DbHelper.MakeUser(id: 1, email: "");
        db.Users.Add(user);
        var order = new Order
        {
            UserId = user.Id,
            Status = OrderStatus.Paid,
            OrderState = OrderState.Paid,
            TotalNok = 500m,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var emailMock = new Mock<IEmailService>();
        var service = CreateService(db, emailMock: emailMock);

        var result = await service.CancelPaymentAsync(order.Id);

        result.Success.Should().BeTrue();
        emailMock.Verify(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CancelPaymentAsync_EmailSendThrows_IsSwallowedAndStillSucceeds()
    {
        var db = DbHelper.CreateInMemory();
        var user = DbHelper.MakeUser(id: 1, email: "customer@example.com");
        db.Users.Add(user);
        var order = new Order
        {
            UserId = user.Id,
            Status = OrderStatus.Paid,
            OrderState = OrderState.Paid,
            TotalNok = 500m,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var emailMock = new Mock<IEmailService>();
        emailMock.Setup(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("SMTP down"));
        var service = CreateService(db, emailMock: emailMock);

        var result = await service.CancelPaymentAsync(order.Id);

        result.Success.Should().BeTrue();
        result.Order!.Status.Should().Be(OrderStatus.Cancelled.ToString());
        emailMock.Verify(e => e.SendAsync(
                "customer@example.com", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
