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
/// Unit tests for the credit-pack branch of <see cref="WebhooksController"/>
/// (BANNERSH-69). Verifies routing logic and idempotency guard for
/// <c>payment_intent.succeeded</c> events.
/// </summary>
public class WebhookCreditPackTests
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

    // ── payment_intent.succeeded: ai_credit_pack ─────────────────────────────

    [Fact]
    public async Task Webhook_AiCreditPack_GrantsCreditsToUser()
    {
        var (controller, stripeMock, creditsMock, _, _) = MakeController();

        stripeMock.Setup(s => s.VerifyAndParseEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeWebhookEvent(
                EventType: "payment_intent.succeeded",
                PaymentIntentId: "pi_pack_001",
                OrderIdFromMetadata: null,
                FailureMessage: null,
                MetadataType: "ai_credit_pack",
                MetadataUserId: 42,
                MetadataCreditCount: 10));

        var result = await controller.Stripe(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();

        creditsMock.Verify(
            c => c.GrantAsync(42, 10, CreditReason.CreditPack, "pi_pack_001", It.IsAny<CancellationToken>()),
            Times.Once,
            "credits must be granted with the PI id as the idempotency referenceId");
    }

    [Fact]
    public async Task Webhook_AiCreditPack_CalledTwice_CallsGrantAsyncTwice_IdempotencyInGrantAsync()
    {
        // The controller calls GrantAsync on each webhook delivery.
        // GrantAsync itself is idempotent (checks ReferenceId before inserting).
        // This test verifies the controller does NOT short-circuit: it delegates
        // idempotency to the service layer, which is the correct design.
        var (controller, stripeMock, creditsMock, _, _) = MakeController();

        var evt = new StripeWebhookEvent(
            EventType: "payment_intent.succeeded",
            PaymentIntentId: "pi_pack_idem",
            OrderIdFromMetadata: null,
            FailureMessage: null,
            MetadataType: "ai_credit_pack",
            MetadataUserId: 42,
            MetadataCreditCount: 10);

        stripeMock.Setup(s => s.VerifyAndParseEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(evt);

        // Simulate two webhook deliveries
        await controller.Stripe(CancellationToken.None);

        // Reset body position between calls (simulate a second HTTP request)
        controller.HttpContext.Request.Body.Position = 0;
        await controller.Stripe(CancellationToken.None);

        // The controller called GrantAsync twice; real AiCreditService deduplicates via ReferenceId.
        creditsMock.Verify(
            c => c.GrantAsync(42, 10, CreditReason.CreditPack, "pi_pack_idem", It.IsAny<CancellationToken>()),
            Times.Exactly(2),
            "controller always delegates to GrantAsync; idempotency lives inside AiCreditService");
    }

    [Fact]
    public async Task Webhook_AiCreditPack_MissingUserId_DoesNotCallGrantAsync()
    {
        var (controller, stripeMock, creditsMock, _, _) = MakeController();

        stripeMock.Setup(s => s.VerifyAndParseEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeWebhookEvent(
                EventType: "payment_intent.succeeded",
                PaymentIntentId: "pi_pack_nouserid",
                OrderIdFromMetadata: null,
                FailureMessage: null,
                MetadataType: "ai_credit_pack",
                MetadataUserId: null,       // metadata missing
                MetadataCreditCount: 10));

        var result = await controller.Stripe(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        creditsMock.Verify(
            c => c.GrantAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CreditReason>(),
                              It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── payment_intent.succeeded: other types ────────────────────────────────

    [Fact]
    public async Task Webhook_BannerOrder_DoesNotGrantCredits()
    {
        var (controller, stripeMock, creditsMock, ordersMock, _) = MakeController();

        stripeMock.Setup(s => s.VerifyAndParseEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeWebhookEvent(
                EventType: "payment_intent.succeeded",
                PaymentIntentId: "pi_order_001",
                OrderIdFromMetadata: 5,
                FailureMessage: null,
                MetadataType: "banner_order"));

        await controller.Stripe(CancellationToken.None);

        creditsMock.Verify(
            c => c.GrantAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CreditReason>(),
                              It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "banner_order events must not trigger credit grants");

        ordersMock.Verify(
            o => o.MarkPaidAsync("pi_order_001", 5, It.IsAny<CancellationToken>()),
            Times.Once,
            "banner_order events must route to the existing order handler");
    }

    [Fact]
    public async Task Webhook_NoMetadataType_RoutesToExistingOrderHandler()
    {
        // Pre-BANNERSH-69 orders had no 'type' in metadata; they must still be processed.
        var (controller, stripeMock, creditsMock, ordersMock, _) = MakeController();

        stripeMock.Setup(s => s.VerifyAndParseEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeWebhookEvent(
                EventType: "payment_intent.succeeded",
                PaymentIntentId: "pi_legacy_order",
                OrderIdFromMetadata: 7,
                FailureMessage: null,
                MetadataType: null));     // no type field

        await controller.Stripe(CancellationToken.None);

        creditsMock.Verify(
            c => c.GrantAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CreditReason>(),
                              It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);

        ordersMock.Verify(
            o => o.MarkPaidAsync("pi_legacy_order", 7, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Webhook_LegacyAiDesignStandalone_DoesNotGrantCreditsOrCallOrderHandler()
    {
        // ai_design_standalone is a retired flow (BANNERSH-67); should be silently ignored.
        var (controller, stripeMock, creditsMock, ordersMock, _) = MakeController();

        stripeMock.Setup(s => s.VerifyAndParseEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeWebhookEvent(
                EventType: "payment_intent.succeeded",
                PaymentIntentId: "pi_legacy_ai",
                OrderIdFromMetadata: null,
                FailureMessage: null,
                MetadataType: "ai_design_standalone"));

        var result = await controller.Stripe(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        creditsMock.Verify(
            c => c.GrantAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CreditReason>(),
                              It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        ordersMock.Verify(
            o => o.MarkPaidAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── payment_intent.payment_failed ────────────────────────────────────────

    [Fact]
    public async Task Webhook_PaymentFailed_DoesNotGrantCredits()
    {
        var (controller, stripeMock, creditsMock, ordersMock, _) = MakeController();

        stripeMock.Setup(s => s.VerifyAndParseEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeWebhookEvent(
                EventType: "payment_intent.payment_failed",
                PaymentIntentId: "pi_failed",
                OrderIdFromMetadata: 3,
                FailureMessage: "Card declined"));

        await controller.Stripe(CancellationToken.None);

        creditsMock.Verify(
            c => c.GrantAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CreditReason>(),
                              It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Invalid signature ─────────────────────────────────────────────────────

    [Fact]
    public async Task Webhook_InvalidSignature_Returns400()
    {
        var (controller, stripeMock, _, _, _) = MakeController();
        stripeMock.Setup(s => s.VerifyAndParseEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StripeWebhookEvent?)null);

        var result = await controller.Stripe(CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
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

    // ── Unknown event type (default branch) ──────────────────────────────────

    [Fact]
    public async Task Webhook_UnknownEventType_Returns200Received()
    {
        var (controller, stripeMock, _, _, _) = MakeController();

        stripeMock.Setup(s => s.VerifyAndParseEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeWebhookEvent(
                EventType: "charge.refunded",       // unknown type
                PaymentIntentId: "pi_unknown",
                OrderIdFromMetadata: null,
                FailureMessage: null,
                MetadataType: null,
                MetadataUserId: null,
                MetadataCreditCount: null));

        var result = await controller.Stripe(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ── Handler exception triggers 500 ──────────────────────────────────────

    [Fact]
    public async Task Webhook_HandlerThrows_Returns500()
    {
        var (controller, stripeMock, _, ordersMock, _) = MakeController();

        stripeMock.Setup(s => s.VerifyAndParseEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeWebhookEvent(
                EventType: "payment_intent.payment_failed",
                PaymentIntentId: "pi_throws",
                OrderIdFromMetadata: null,
                FailureMessage: "Declined",
                MetadataType: null,
                MetadataUserId: null,
                MetadataCreditCount: null));

        // Make the order service throw so the catch block is hit
        ordersMock.Setup(o => o.MarkPaymentFailedAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        var result = await controller.Stripe(CancellationToken.None);

        ((ObjectResult)result).StatusCode.Should().Be(500);
    }
}
