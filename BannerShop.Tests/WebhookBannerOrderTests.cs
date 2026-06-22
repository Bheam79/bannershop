using System.Text;
using BannerShop.Api.Controllers;
using BannerShop.Api.Services.AiCredits;
using BannerShop.Api.Services.DesignRequests;
using BannerShop.Api.Services.Orders;
using BannerShop.Api.Services.Orders.Stripe;
using BannerShop.Core.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BannerShop.Tests;

/// <summary>
/// Unit tests for the <c>payment_intent.amount_capturable_updated</c> branch
/// of <see cref="WebhooksController"/> (BANNERSH-279).
/// Verifies that banner-order payment confirmation is wired correctly and that
/// AI-credit logic is never triggered by this event type.
/// </summary>
public class WebhookBannerOrderTests
{
    // ── Controller factory ───────────────────────────────────────────────────

    private static (WebhooksController controller,
                    Mock<IStripePaymentService> stripeMock,
                    Mock<IAiCreditService> creditsMock,
                    Mock<IOrderService> ordersMock,
                    Mock<IDesignRequestService> designMock)
        MakeController()
    {
        var stripeMock  = new Mock<IStripePaymentService>();
        var creditsMock = new Mock<IAiCreditService>();
        var ordersMock  = new Mock<IOrderService>();
        var designMock  = new Mock<IDesignRequestService>();

        // Default mocks: all handlers succeed silently
        creditsMock.Setup(c => c.GrantAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CreditReason>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        ordersMock.Setup(o => o.MarkPaidAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        designMock.Setup(d => d.MarkPaidAndEnqueueAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var controller = new WebhooksController(
            stripeMock.Object,
            ordersMock.Object,
            designMock.Object,
            creditsMock.Object,
            NullLogger<WebhooksController>.Instance);

        // Set up a minimal HTTP context: MemoryStream body + Stripe-Signature header.
        // EnableBuffering() is a no-op for seekable streams (MemoryStream), so this
        // satisfies the controller's body-reading logic without a full HTTP pipeline.
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        httpContext.Request.Headers["Stripe-Signature"] = "test-sig";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        return (controller, stripeMock, creditsMock, ordersMock, designMock);
    }

    // ── payment_intent.amount_capturable_updated ─────────────────────────────

    [Fact]
    public async Task Webhook_AmountCapturableUpdated_CallsMarkPaidAsyncWithCorrectArgs()
    {
        var (controller, stripeMock, creditsMock, ordersMock, _) = MakeController();

        stripeMock.Setup(s => s.VerifyAndParseEventAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeWebhookEvent(
                EventType: "payment_intent.amount_capturable_updated",
                PaymentIntentId: "pi_capture_001",
                OrderIdFromMetadata: 99,
                FailureMessage: null));

        var result = await controller.Stripe(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();

        ordersMock.Verify(
            o => o.MarkPaidAsync("pi_capture_001", 99, It.IsAny<CancellationToken>()),
            Times.Once,
            "amount_capturable_updated must call MarkPaidAsync with PI id and order-id hint");

        creditsMock.Verify(
            c => c.GrantAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CreditReason>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "amount_capturable_updated must never grant AI credits");
    }

    [Fact]
    public async Task Webhook_AmountCapturableUpdated_NullOrderId_StillCallsMarkPaidAsync()
    {
        var (controller, stripeMock, _, ordersMock, _) = MakeController();

        stripeMock.Setup(s => s.VerifyAndParseEventAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeWebhookEvent(
                EventType: "payment_intent.amount_capturable_updated",
                PaymentIntentId: "pi_capture_noid",
                OrderIdFromMetadata: null,   // no hint in metadata
                FailureMessage: null));

        var result = await controller.Stripe(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        ordersMock.Verify(
            o => o.MarkPaidAsync("pi_capture_noid", null, It.IsAny<CancellationToken>()),
            Times.Once,
            "MarkPaidAsync must be called even when no order-id hint is in metadata");
    }

    [Fact]
    public async Task Webhook_AmountCapturableUpdated_HandlerThrows_Returns500()
    {
        var (controller, stripeMock, _, ordersMock, _) = MakeController();

        stripeMock.Setup(s => s.VerifyAndParseEventAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeWebhookEvent(
                EventType: "payment_intent.amount_capturable_updated",
                PaymentIntentId: "pi_capture_throws",
                OrderIdFromMetadata: 7,
                FailureMessage: null));

        ordersMock.Setup(o => o.MarkPaidAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB timeout"));

        var result = await controller.Stripe(CancellationToken.None);

        ((ObjectResult)result).StatusCode.Should().Be(500,
            "a handler exception must return 500 so Stripe retries delivery");
    }

    // ── Missing Stripe-Signature header ──────────────────────────────────────

    [Fact]
    public async Task Webhook_MissingSignatureHeader_Returns400()
    {
        var (_, stripeMock, creditsMock, ordersMock, designMock) = MakeController();

        // Build controller without Stripe-Signature header
        var controller = new WebhooksController(
            stripeMock.Object,
            ordersMock.Object,
            designMock.Object,
            creditsMock.Object,
            NullLogger<WebhooksController>.Instance);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        // Deliberately NOT setting Stripe-Signature header
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.Stripe(CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        var bad = (BadRequestObjectResult)result;
        bad.Value.ToString().Should().Contain("Stripe-Signature");
    }
}
