using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using BannerShop.Api.Services.Orders.Stripe;
using BannerShop.Api.Services.SystemSettings;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BannerShop.Tests;

/// <summary>
/// Unit tests for <see cref="StripePaymentService"/> (BANNERSH-161). The class is
/// <c>[ExcludeFromCodeCoverage]</c> as "tested via integration" because most methods
/// call the real Stripe HTTP API, but two paths are pure/offline-testable and had
/// ZERO direct test refs (only mocked away entirely in <c>WebhookCreditPackTests</c>
/// / <c>WebhookBannerOrderTests</c>):
///   1. Key-configuration gating — <c>GetEffectiveSecretKeyAsync</c> throws BEFORE any
///      Stripe HTTP call is attempted when the db key is unset/placeholder.
///   2. <c>VerifyAndParseEventAsync</c> — signature verification is pure HMAC-SHA256
///      (no network call), so a real signed payload can be constructed here and fed
///      through the actual Stripe.net <c>EventUtility.ConstructEvent</c> parser.
/// </summary>
public class StripePaymentServiceTests
{
    private static StripePaymentService CreateService(ISystemSettingsService settings)
        => new(Options.Create(new StripeOptions()), settings, NullLogger<StripePaymentService>.Instance);

    // ── Key-configuration gating ─────────────────────────────────────────────

    [Fact]
    public async Task CreatePaymentIntentAsync_KeyNotConfigured_ThrowsBeforeAnyNetworkCall()
    {
        var service = CreateService(new StubSettings());

        var act = () => service.CreatePaymentIntentAsync(1, 1, 100m);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not configured*");
    }

    [Fact]
    public async Task CreatePaymentIntentAsync_PlaceholderKey_TreatedAsUnconfigured()
    {
        var service = CreateService(new StubSettings(secretKey: "sk_test_REPLACE_ME"));

        var act = () => service.CreatePaymentIntentAsync(1, 1, 100m);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CreateCreditPackPaymentIntentAsync_KeyNotConfigured_ThrowsBeforeAnyNetworkCall()
    {
        var service = CreateService(new StubSettings());

        var act = () => service.CreateCreditPackPaymentIntentAsync(1, 10, 199m, "idem-key");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RetrievePaymentIntentAsync_KeyNotConfigured_PropagatesUnlikeStripeExceptions()
    {
        // Contrast: a real StripeException from the API is caught and swallowed to
        // null (see the test below). A config error is a bug, not an expected
        // Stripe-side failure, so it must NOT be silently swallowed the same way.
        var service = CreateService(new StubSettings());

        var act = () => service.RetrievePaymentIntentAsync("pi_123");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RetrievePaymentIntentAsync_BlankPaymentIntentId_ReturnsNullWithoutCheckingKey()
    {
        // Settings deliberately empty (would throw if the key were looked up) —
        // proves the blank-id guard short-circuits before key resolution.
        var service = CreateService(new StubSettings());

        var result = await service.RetrievePaymentIntentAsync("   ");

        result.Should().BeNull();
    }

    [Fact]
    public async Task IsPaymentIntentSucceededAsync_BlankPaymentIntentId_ReturnsFalseWithoutCheckingKey()
    {
        var service = CreateService(new StubSettings());

        var result = await service.IsPaymentIntentSucceededAsync(string.Empty);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CapturePaymentIntentAsync_KeyNotConfigured_PropagatesUnlikeStripeExceptions()
    {
        var service = CreateService(new StubSettings());

        var act = () => service.CapturePaymentIntentAsync("pi_123");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── VerifyAndParseEventAsync: webhook secret gating ──────────────────────

    [Fact]
    public async Task VerifyAndParseEventAsync_WebhookSecretNotConfigured_ReturnsNull()
    {
        var service = CreateService(new StubSettings());

        var result = await service.VerifyAndParseEventAsync("{}", "t=1,v1=deadbeef");

        result.Should().BeNull();
    }

    [Fact]
    public async Task VerifyAndParseEventAsync_WebhookSecretIsPlaceholder_ReturnsNull()
    {
        var service = CreateService(new StubSettings(webhookSecret: "whsec_REPLACE_ME"));

        var result = await service.VerifyAndParseEventAsync("{}", "t=1,v1=deadbeef");

        result.Should().BeNull();
    }

    // ── VerifyAndParseEventAsync: real signature verification ───────────────

    [Fact]
    public async Task VerifyAndParseEventAsync_ValidBannerOrderEvent_ParsesOrderIdAndType()
    {
        const string secret = "whsec_test_secret_123";
        var service = CreateService(new StubSettings(webhookSecret: secret));

        var (body, header) = SignedEvent(secret, "payment_intent.succeeded",
            MakePaymentIntent("pi_order_1", "succeeded",
                metadata: new() { ["orderId"] = "5", ["type"] = "banner_order" }));

        var result = await service.VerifyAndParseEventAsync(body, header);

        result.Should().NotBeNull();
        result!.EventType.Should().Be("payment_intent.succeeded");
        result.PaymentIntentId.Should().Be("pi_order_1");
        result.OrderIdFromMetadata.Should().Be(5);
        result.MetadataType.Should().Be("banner_order");
        result.MetadataUserId.Should().BeNull();
        result.MetadataCreditCount.Should().BeNull();
        result.FailureMessage.Should().BeNull();
    }

    [Fact]
    public async Task VerifyAndParseEventAsync_ValidCreditPackEvent_ParsesUserIdAndCreditCount()
    {
        const string secret = "whsec_test_secret_123";
        var service = CreateService(new StubSettings(webhookSecret: secret));

        var (body, header) = SignedEvent(secret, "payment_intent.succeeded",
            MakePaymentIntent("pi_pack_1", "succeeded",
                metadata: new() { ["type"] = "ai_credit_pack", ["userId"] = "42", ["creditCount"] = "10" }));

        var result = await service.VerifyAndParseEventAsync(body, header);

        result.Should().NotBeNull();
        result!.MetadataType.Should().Be("ai_credit_pack");
        result.MetadataUserId.Should().Be(42);
        result.MetadataCreditCount.Should().Be(10);
        result.OrderIdFromMetadata.Should().BeNull();
    }

    [Fact]
    public async Task VerifyAndParseEventAsync_PaymentFailedEvent_ParsesFailureMessage()
    {
        const string secret = "whsec_test_secret_123";
        var service = CreateService(new StubSettings(webhookSecret: secret));

        var (body, header) = SignedEvent(secret, "payment_intent.payment_failed",
            MakePaymentIntent("pi_failed_1", "requires_payment_method",
                metadata: new() { ["orderId"] = "9" },
                lastErrorMessage: "Your card was declined."));

        var result = await service.VerifyAndParseEventAsync(body, header);

        result.Should().NotBeNull();
        result!.FailureMessage.Should().Be("Your card was declined.");
        result.OrderIdFromMetadata.Should().Be(9);
    }

    [Fact]
    public async Task VerifyAndParseEventAsync_WrongSecret_ReturnsNull()
    {
        var service = CreateService(new StubSettings(webhookSecret: "whsec_real_secret"));

        var (body, header) = SignedEvent("whsec_different_secret", "payment_intent.succeeded",
            MakePaymentIntent("pi_1", "succeeded"));

        var result = await service.VerifyAndParseEventAsync(body, header);

        result.Should().BeNull();
    }

    [Fact]
    public async Task VerifyAndParseEventAsync_TamperedPayload_ReturnsNull()
    {
        const string secret = "whsec_test_secret_123";
        var service = CreateService(new StubSettings(webhookSecret: secret));

        var (body, header) = SignedEvent(secret, "payment_intent.succeeded",
            MakePaymentIntent("pi_1", "succeeded", metadata: new() { ["orderId"] = "5" }));

        // Attacker swaps the orderId after the signature was computed over the original body.
        var tampered = body.Replace("\"orderId\":\"5\"", "\"orderId\":\"999\"");

        var result = await service.VerifyAndParseEventAsync(tampered, header);

        result.Should().BeNull();
    }

    [Fact]
    public async Task VerifyAndParseEventAsync_NonPaymentIntentObject_ReturnsEventWithEmptyIdAndNullMetadata()
    {
        const string secret = "whsec_test_secret_123";
        var service = CreateService(new StubSettings(webhookSecret: secret));

        var charge = new JsonObject { ["id"] = "ch_1", ["object"] = "charge" };
        var (body, header) = SignedEvent(secret, "charge.refunded", charge);

        var result = await service.VerifyAndParseEventAsync(body, header);

        result.Should().NotBeNull();
        result!.EventType.Should().Be("charge.refunded");
        result.PaymentIntentId.Should().Be(string.Empty);
        result.OrderIdFromMetadata.Should().BeNull();
        result.MetadataType.Should().BeNull();
    }

    [Fact]
    public async Task VerifyAndParseEventAsync_NonNumericOrderIdMetadata_OrderIdFromMetadataIsNull()
    {
        const string secret = "whsec_test_secret_123";
        var service = CreateService(new StubSettings(webhookSecret: secret));

        var (body, header) = SignedEvent(secret, "payment_intent.succeeded",
            MakePaymentIntent("pi_1", "succeeded", metadata: new() { ["orderId"] = "not-a-number" }));

        var result = await service.VerifyAndParseEventAsync(body, header);

        result.Should().NotBeNull();
        result!.OrderIdFromMetadata.Should().BeNull();
    }

    [Fact]
    public async Task VerifyAndParseEventAsync_NoMetadataAtAll_AllMetadataFieldsNull()
    {
        const string secret = "whsec_test_secret_123";
        var service = CreateService(new StubSettings(webhookSecret: secret));

        var (body, header) = SignedEvent(secret, "payment_intent.succeeded",
            MakePaymentIntent("pi_1", "succeeded", metadata: null));

        var result = await service.VerifyAndParseEventAsync(body, header);

        result.Should().NotBeNull();
        result!.PaymentIntentId.Should().Be("pi_1");
        result.OrderIdFromMetadata.Should().BeNull();
        result.MetadataType.Should().BeNull();
        result.MetadataUserId.Should().BeNull();
        result.MetadataCreditCount.Should().BeNull();
    }

    // ── Signed-event construction helpers ────────────────────────────────────

    private static JsonObject MakePaymentIntent(
        string id, string status, Dictionary<string, string>? metadata = null, string? lastErrorMessage = null)
    {
        var obj = new JsonObject
        {
            ["id"] = id,
            ["object"] = "payment_intent",
            ["status"] = status
        };
        if (metadata is not null)
        {
            var meta = new JsonObject();
            foreach (var (k, v) in metadata) meta[k] = v;
            obj["metadata"] = meta;
        }
        if (lastErrorMessage is not null)
            obj["last_payment_error"] = new JsonObject { ["message"] = lastErrorMessage };
        return obj;
    }

    /// <summary>
    /// Builds a real Stripe Event JSON body plus a matching valid Stripe-Signature
    /// header (HMAC-SHA256 over "{timestamp}.{payload}", the documented Stripe
    /// webhook signing scheme) — entirely offline, no Stripe API call involved.
    /// </summary>
    private static (string Body, string SignatureHeader) SignedEvent(
        string secret, string eventType, JsonObject dataObject)
    {
        var evt = new JsonObject
        {
            ["id"] = "evt_test_1",
            ["object"] = "event",
            ["api_version"] = "2024-06-20",
            ["created"] = 1700000000,
            ["livemode"] = false,
            ["pending_webhooks"] = 1,
            ["request"] = new JsonObject { ["id"] = null, ["idempotency_key"] = null },
            ["type"] = eventType,
            ["data"] = new JsonObject { ["object"] = dataObject }
        };
        var body = evt.ToJsonString();

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        var signature = Convert.ToHexString(hash).ToLowerInvariant();

        return (body, $"t={timestamp},v1={signature}");
    }

    // ── Test double ───────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal <see cref="ISystemSettingsService"/> exposing just the two Stripe
    /// rows this service reads ('stripe_secret_key' / 'stripe_webhook_secret').
    /// </summary>
    private sealed class StubSettings : ISystemSettingsService
    {
        private readonly string? _secretKey;
        private readonly string? _webhookSecret;

        public StubSettings(string? secretKey = null, string? webhookSecret = null)
        {
            _secretKey = secretKey;
            _webhookSecret = webhookSecret;
        }

        public Task<string?> GetValueAsync(string key, CancellationToken ct = default) => key switch
        {
            "stripe_secret_key" => Task.FromResult(_secretKey),
            "stripe_webhook_secret" => Task.FromResult(_webhookSecret),
            _ => Task.FromResult<string?>(null)
        };

        public Task SetValueAsync(string key, string value, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<SystemSettingDto>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SystemSettingDto>>(Array.Empty<SystemSettingDto>());
    }
}
