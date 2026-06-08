using BannerShop.Api.Services.AiCredits;
using BannerShop.Core.Enums;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BannerShop.Tests;

public class AiCreditServiceTests
{
    private static AiCreditService MakeService(BannerShop.Infrastructure.Data.BannerShopDbContext db)
        => new(db, NullLogger<AiCreditService>.Instance);

    // ── IsAnonymousEligibleAsync ─────────────────────────────────────────────

    [Fact]
    public async Task IsAnonymousEligibleAsync_returns_true_for_new_ip()
    {
        using var db = DbHelper.CreateInMemory();
        var svc = MakeService(db);

        var eligible = await svc.IsAnonymousEligibleAsync("192.0.2.1");

        eligible.Should().BeTrue();
    }

    [Fact]
    public async Task IsAnonymousEligibleAsync_returns_false_after_usage_within_window()
    {
        using var db = DbHelper.CreateInMemory();
        var svc = MakeService(db);

        db.IpAiUsages.Add(new BannerShop.Core.Entities.IpAiUsage
        {
            IpAddress = "192.0.2.2",
            CreatedAt = DateTime.UtcNow.AddDays(-5) // 5 days ago — within 30-day window
        });
        await db.SaveChangesAsync();

        var eligible = await svc.IsAnonymousEligibleAsync("192.0.2.2");

        eligible.Should().BeFalse();
    }

    [Fact]
    public async Task IsAnonymousEligibleAsync_returns_true_after_usage_outside_window()
    {
        using var db = DbHelper.CreateInMemory();
        var svc = MakeService(db);

        db.IpAiUsages.Add(new BannerShop.Core.Entities.IpAiUsage
        {
            IpAddress = "192.0.2.3",
            CreatedAt = DateTime.UtcNow.AddDays(-31) // 31 days ago — outside 30-day window
        });
        await db.SaveChangesAsync();

        var eligible = await svc.IsAnonymousEligibleAsync("192.0.2.3");

        eligible.Should().BeTrue();
    }

    // ── RecordAnonymousUsageAsync ────────────────────────────────────────────

    [Fact]
    public async Task RecordAnonymousUsageAsync_creates_IpAiUsage_and_transaction_row()
    {
        using var db = DbHelper.CreateInMemory();
        var svc = MakeService(db);

        await svc.RecordAnonymousUsageAsync("10.0.0.1");

        db.IpAiUsages.Should().ContainSingle(u => u.IpAddress == "10.0.0.1");
        db.AiCreditTransactions.Should().ContainSingle(t =>
            t.IpAddress == "10.0.0.1" &&
            t.Amount == -1 &&
            t.Reason == CreditReason.FreeAnonymous);
    }

    // ── TryConsumeAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task TryConsumeAsync_deducts_credits_and_returns_true()
    {
        using var db = DbHelper.CreateInMemory();
        var user = DbHelper.MakeUser(1);
        user.AiCreditsRemaining = 5;
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var svc = MakeService(db);

        var result = await svc.TryConsumeAsync(1, count: 1);

        result.Should().BeTrue();
        db.Users.Find(1)!.AiCreditsRemaining.Should().Be(4);
        db.AiCreditTransactions.Should().ContainSingle(t =>
            t.UserId == 1 && t.Amount == -1 && t.Reason == CreditReason.Consumed);
    }

    [Fact]
    public async Task TryConsumeAsync_returns_false_when_insufficient_credits()
    {
        using var db = DbHelper.CreateInMemory();
        var user = DbHelper.MakeUser(1);
        user.AiCreditsRemaining = 0;
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var svc = MakeService(db);

        var result = await svc.TryConsumeAsync(1, count: 1);

        result.Should().BeFalse();
        db.Users.Find(1)!.AiCreditsRemaining.Should().Be(0); // unchanged
        db.AiCreditTransactions.Should().BeEmpty(); // no transaction written
    }

    [Fact]
    public async Task TryConsumeAsync_returns_false_for_unknown_user()
    {
        using var db = DbHelper.CreateInMemory();
        var svc = MakeService(db);

        var result = await svc.TryConsumeAsync(9999, count: 1);

        result.Should().BeFalse();
    }

    // ── GrantAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GrantAsync_adds_credits_to_user_balance()
    {
        using var db = DbHelper.CreateInMemory();
        var user = DbHelper.MakeUser(1);
        user.AiCreditsRemaining = 0;
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var svc = MakeService(db);

        await svc.GrantAsync(1, count: 10, CreditReason.CreditPack, referenceId: "pi_test_abc");

        db.Users.Find(1)!.AiCreditsRemaining.Should().Be(10);
        db.AiCreditTransactions.Should().ContainSingle(t =>
            t.UserId == 1 &&
            t.Amount == 10 &&
            t.Reason == CreditReason.CreditPack &&
            t.ReferenceId == "pi_test_abc");
    }

    [Fact]
    public async Task GrantAsync_is_idempotent_for_same_referenceId()
    {
        using var db = DbHelper.CreateInMemory();
        var user = DbHelper.MakeUser(1);
        user.AiCreditsRemaining = 0;
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var svc = MakeService(db);

        // First grant
        await svc.GrantAsync(1, count: 10, CreditReason.CreditPack, referenceId: "pi_idempotent");
        // Second grant with same referenceId — should be a no-op
        await svc.GrantAsync(1, count: 10, CreditReason.CreditPack, referenceId: "pi_idempotent");

        // Credits only granted once
        db.Users.Find(1)!.AiCreditsRemaining.Should().Be(10);
        // Only one transaction row written
        db.AiCreditTransactions
            .Where(t => t.ReferenceId == "pi_idempotent")
            .Should().HaveCount(1);
    }

    [Fact]
    public async Task GrantAsync_without_referenceId_allows_multiple_grants()
    {
        using var db = DbHelper.CreateInMemory();
        var user = DbHelper.MakeUser(1);
        user.AiCreditsRemaining = 0;
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var svc = MakeService(db);

        await svc.GrantAsync(1, count: 5, CreditReason.FreeAuthenticated, referenceId: null);
        await svc.GrantAsync(1, count: 5, CreditReason.FreeAuthenticated, referenceId: null);

        // Both grants applied because referenceId is null
        db.Users.Find(1)!.AiCreditsRemaining.Should().Be(10);
    }

    // ── GetBalanceAsync / GetBalanceWithDetailsAsync ─────────────────────────

    [Fact]
    public async Task GetBalanceAsync_returns_current_balance()
    {
        using var db = DbHelper.CreateInMemory();
        var user = DbHelper.MakeUser(1);
        user.AiCreditsRemaining = 7;
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var svc = MakeService(db);

        var balance = await svc.GetBalanceAsync(1);
        balance.Should().Be(7);
    }

    [Fact]
    public async Task GetBalanceWithDetailsAsync_returns_both_fields()
    {
        using var db = DbHelper.CreateInMemory();
        var user = DbHelper.MakeUser(1);
        user.AiCreditsRemaining = 3;
        user.HasUsedFreeAiGeneration = true;
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var svc = MakeService(db);

        var dto = await svc.GetBalanceWithDetailsAsync(1);
        dto.CreditsRemaining.Should().Be(3);
        dto.HasUsedFreeGeneration.Should().BeTrue();
    }

    [Fact]
    public async Task GetBalanceWithDetailsAsync_returns_zero_for_unknown_user()
    {
        using var db = DbHelper.CreateInMemory();
        var svc = MakeService(db);

        var dto = await svc.GetBalanceWithDetailsAsync(9999);
        dto.CreditsRemaining.Should().Be(0);
        dto.HasUsedFreeGeneration.Should().BeFalse();
    }
}
