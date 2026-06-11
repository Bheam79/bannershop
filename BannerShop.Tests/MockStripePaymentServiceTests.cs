using BannerShop.Api.Services.Orders.Stripe;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BannerShop.Tests;

/// <summary>
/// Unit tests for MockStripePaymentService (the in-process stub used in tests and dev).
/// </summary>
public class MockStripePaymentServiceTests
{
    private static MockStripePaymentService Make()
        => new MockStripePaymentService(NullLogger<MockStripePaymentService>.Instance);

    // ── CreatePaymentIntentAsync ──────────────────────────────────────────────

    [Fact]
    public async Task CreatePaymentIntentAsync_ReturnsNonNullIds()
    {
        var svc = Make();

        var result = await svc.CreatePaymentIntentAsync(orderId: 42, userId: 1, amountNok: 500m);

        result.PaymentIntentId.Should().NotBeNullOrEmpty();
        result.ClientSecret.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreatePaymentIntentAsync_IncludesOrderIdInIntentId()
    {
        var svc = Make();

        var result = await svc.CreatePaymentIntentAsync(orderId: 123, userId: 1, amountNok: 100m);

        result.PaymentIntentId.Should().Contain("123");
    }

    // ── CreateCreditPackPaymentIntentAsync ────────────────────────────────────

    [Fact]
    public async Task CreateCreditPackPaymentIntentAsync_ReturnsNonNullIds()
    {
        var svc = Make();

        var result = await svc.CreateCreditPackPaymentIntentAsync(
            userId: 5, creditCount: 10, amountNok: 29m,
            idempotencyKey: "1234567890abcdef", orderId: null);

        result.PaymentIntentId.Should().NotBeNullOrEmpty();
        result.ClientSecret.Should().NotBeNullOrEmpty();
    }

    // ── UpdatePaymentIntentAmountAsync ────────────────────────────────────────

    [Fact]
    public async Task UpdatePaymentIntentAmountAsync_ReturnsUpdatedIds()
    {
        var svc = Make();
        const string piId = "pi_mock_42";

        var result = await svc.UpdatePaymentIntentAmountAsync(piId, amountNok: 600m);

        result.PaymentIntentId.Should().Be(piId);
        result.ClientSecret.Should().NotBeNullOrEmpty();
    }

    // ── CancelPaymentIntentAsync ──────────────────────────────────────────────

    [Fact]
    public async Task CancelPaymentIntentAsync_DoesNotThrow()
    {
        var svc = Make();
        var act = async () => await svc.CancelPaymentIntentAsync("pi_mock_42");
        await act.Should().NotThrowAsync();
    }

    // ── IsPaymentIntentSucceededAsync ─────────────────────────────────────────

    [Fact]
    public async Task IsPaymentIntentSucceededAsync_NonEmptyId_ReturnsTrue()
    {
        var svc = Make();

        var result = await svc.IsPaymentIntentSucceededAsync("pi_mock_42");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsPaymentIntentSucceededAsync_EmptyId_ReturnsFalse()
    {
        var svc = Make();

        var result = await svc.IsPaymentIntentSucceededAsync(string.Empty);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsPaymentIntentSucceededAsync_NullId_ReturnsFalse()
    {
        var svc = Make();

        var result = await svc.IsPaymentIntentSucceededAsync(null!);

        result.Should().BeFalse();
    }

    // ── RetrievePaymentIntentAsync ────────────────────────────────────────────

    [Fact]
    public async Task RetrievePaymentIntentAsync_NonEmptyId_ReturnsNonNull()
    {
        var svc = Make();

        var result = await svc.RetrievePaymentIntentAsync("pi_mock_42");

        result.Should().NotBeNull();
        result!.PaymentIntentId.Should().Be("pi_mock_42");
    }

    [Fact]
    public async Task RetrievePaymentIntentAsync_EmptyId_ReturnsNull()
    {
        var svc = Make();

        var result = await svc.RetrievePaymentIntentAsync(string.Empty);

        result.Should().BeNull();
    }

    // ── VerifyAndParseEventAsync ──────────────────────────────────────────────

    [Fact]
    public async Task VerifyAndParseEventAsync_AlwaysReturnsNull()
    {
        var svc = Make();

        var result = await svc.VerifyAndParseEventAsync("body", "sig");

        result.Should().BeNull();
    }
}
