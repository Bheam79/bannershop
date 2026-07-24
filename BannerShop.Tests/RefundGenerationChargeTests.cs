using BannerShop.Api.Services.AiCredits;
using BannerShop.Core.Enums;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BannerShop.Tests;

/// <summary>
/// Covers <see cref="AiCreditService.RefundGenerationChargeAsync"/> (BANNERSH-288):
/// a generation attempt blocked by OpenAI moderation must reverse whatever the
/// customer was charged — a paid credit, their one-time free authenticated try,
/// or their anonymous per-IP free try — and never double-refund on retry.
/// </summary>
public class RefundGenerationChargeTests
{
    private static AiCreditService MakeService(BannerShop.Infrastructure.Data.BannerShopDbContext db)
        => new(db, NullLogger<AiCreditService>.Instance);

    [Fact]
    public async Task None_is_noop()
    {
        using var db = DbHelper.CreateInMemory();
        var user = DbHelper.MakeUser(1);
        user.AiCreditsRemaining = 2;
        user.HasUsedFreeAiGeneration = true;
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var svc = MakeService(db);

        await svc.RefundGenerationChargeAsync(1, ipAddress: null, AiChargeKind.None, referenceId: "dr:1");

        // Nothing reversed, no audit row written.
        db.Users.Find(1)!.AiCreditsRemaining.Should().Be(2);
        db.Users.Find(1)!.HasUsedFreeAiGeneration.Should().BeTrue();
        db.AiCreditTransactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Consumed_grants_one_credit_back()
    {
        using var db = DbHelper.CreateInMemory();
        var user = DbHelper.MakeUser(1);
        user.AiCreditsRemaining = 0;
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var svc = MakeService(db);

        await svc.RefundGenerationChargeAsync(1, ipAddress: null, AiChargeKind.Consumed, referenceId: "dr:7");

        db.Users.Find(1)!.AiCreditsRemaining.Should().Be(1);
        db.AiCreditTransactions.Should().ContainSingle(t =>
            t.UserId == 1 &&
            t.Amount == 1 &&
            t.Reason == CreditReason.Refunded &&
            t.ReferenceId == "dr:7");
    }

    [Fact]
    public async Task FreeAuthenticated_clears_flag_and_writes_zero_amount_row()
    {
        using var db = DbHelper.CreateInMemory();
        var user = DbHelper.MakeUser(1);
        user.AiCreditsRemaining = 3;
        user.HasUsedFreeAiGeneration = true;
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var svc = MakeService(db);

        await svc.RefundGenerationChargeAsync(1, ipAddress: null, AiChargeKind.FreeAuthenticated, referenceId: "dr:9");

        db.Users.Find(1)!.HasUsedFreeAiGeneration.Should().BeFalse();
        db.Users.Find(1)!.AiCreditsRemaining.Should().Be(3); // credits untouched
        db.AiCreditTransactions.Should().ContainSingle(t =>
            t.UserId == 1 &&
            t.Amount == 0 &&
            t.Reason == CreditReason.Refunded &&
            t.ReferenceId == "dr:9");
    }

    [Fact]
    public async Task FreeAuthenticated_unknown_user_is_noop()
    {
        using var db = DbHelper.CreateInMemory();
        var svc = MakeService(db);

        await svc.RefundGenerationChargeAsync(9999, ipAddress: null, AiChargeKind.FreeAuthenticated, referenceId: "dr:x");

        db.AiCreditTransactions.Should().BeEmpty();
    }

    [Fact]
    public async Task FreeAnonymous_removes_latest_usage_and_writes_refund_row()
    {
        using var db = DbHelper.CreateInMemory();
        db.IpAiUsages.AddRange(
            new BannerShop.Core.Entities.IpAiUsage { IpAddress = "203.0.113.5", CreatedAt = DateTime.UtcNow.AddDays(-3) },
            new BannerShop.Core.Entities.IpAiUsage { IpAddress = "203.0.113.5", CreatedAt = DateTime.UtcNow.AddMinutes(-1) }
        );
        await db.SaveChangesAsync();

        var svc = MakeService(db);

        await svc.RefundGenerationChargeAsync(userId: null, ipAddress: "203.0.113.5", AiChargeKind.FreeAnonymous, referenceId: "dr:11");

        // Only the most-recent usage row is removed (the one this attempt created).
        db.IpAiUsages.Where(u => u.IpAddress == "203.0.113.5").Should().ContainSingle()
            .Which.CreatedAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(-3), TimeSpan.FromMinutes(1));
        db.AiCreditTransactions.Should().ContainSingle(t =>
            t.IpAddress == "203.0.113.5" &&
            t.Amount == 0 &&
            t.Reason == CreditReason.Refunded &&
            t.ReferenceId == "dr:11");
    }

    [Fact]
    public async Task FreeAnonymous_without_ip_is_noop()
    {
        using var db = DbHelper.CreateInMemory();
        var svc = MakeService(db);

        await svc.RefundGenerationChargeAsync(userId: null, ipAddress: null, AiChargeKind.FreeAnonymous, referenceId: "dr:y");

        db.AiCreditTransactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Is_idempotent_for_duplicate_referenceId()
    {
        using var db = DbHelper.CreateInMemory();
        var user = DbHelper.MakeUser(1);
        user.AiCreditsRemaining = 5;
        user.HasUsedFreeAiGeneration = true;
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var svc = MakeService(db);

        // First refund clears the free-generation flag and writes one audit row.
        await svc.RefundGenerationChargeAsync(1, ipAddress: null, AiChargeKind.FreeAuthenticated, referenceId: "dr:dup");
        // Second refund with the same referenceId must be a no-op.
        await svc.RefundGenerationChargeAsync(1, ipAddress: null, AiChargeKind.FreeAuthenticated, referenceId: "dr:dup");

        db.AiCreditTransactions.Where(t => t.ReferenceId == "dr:dup").Should().HaveCount(1);
    }
}
