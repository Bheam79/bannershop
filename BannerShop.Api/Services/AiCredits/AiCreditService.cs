using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Services.AiCredits;

/// <summary>
/// EF-backed implementation of <see cref="IAiCreditService"/>.
/// All public methods are safe to call concurrently — they use optimistic row-level logic
/// sufficient for the expected v1 request rate.
/// </summary>
public sealed class AiCreditService : IAiCreditService
{
    private static readonly TimeSpan RollingWindow = TimeSpan.FromDays(30);

    private readonly BannerShopDbContext _db;
    private readonly ILogger<AiCreditService> _log;

    public AiCreditService(BannerShopDbContext db, ILogger<AiCreditService> log)
    {
        _db = db;
        _log = log;
    }

    /// <summary>Maximum number of free anonymous AI generations allowed per IP per rolling window.</summary>
    private const int AnonymousFreeLimit = 2;

    /// <inheritdoc />
    public async Task<bool> IsAnonymousEligibleAsync(string ipAddress, CancellationToken ct = default)
    {
        var windowStart = DateTime.UtcNow - RollingWindow;
        var usedInWindow = await _db.IpAiUsages
            .AsNoTracking()
            .CountAsync(u => u.IpAddress == ipAddress && u.CreatedAt >= windowStart, ct);
        return usedInWindow < AnonymousFreeLimit;
    }

    /// <inheritdoc />
    public async Task RecordAnonymousUsageAsync(string ipAddress, CancellationToken ct = default)
    {
        _db.IpAiUsages.Add(new IpAiUsage
        {
            IpAddress = ipAddress,
            CreatedAt = DateTime.UtcNow
        });

        // Also write a transaction record for the audit log.
        _db.AiCreditTransactions.Add(new AiCreditTransaction
        {
            UserId = null,
            IpAddress = ipAddress,
            Amount = -1,
            Reason = CreditReason.FreeAnonymous,
            ReferenceId = null,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<bool> TryConsumeAsync(int userId, int count = 1, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            _log.LogWarning("TryConsumeAsync: user {UserId} not found.", userId);
            return false;
        }

        if (user.AiCreditsRemaining < count)
            return false;

        user.AiCreditsRemaining -= count;

        _db.AiCreditTransactions.Add(new AiCreditTransaction
        {
            UserId = userId,
            Amount = -count,
            Reason = CreditReason.Consumed,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <inheritdoc />
    public async Task GrantAsync(int userId, int count, CreditReason reason, string? referenceId = null, CancellationToken ct = default)
    {
        // Idempotency check: if a non-null referenceId already exists, skip.
        if (!string.IsNullOrEmpty(referenceId))
        {
            var alreadyGranted = await _db.AiCreditTransactions
                .AsNoTracking()
                .AnyAsync(t => t.ReferenceId == referenceId, ct);

            if (alreadyGranted)
            {
                _log.LogDebug("GrantAsync: duplicate referenceId {Ref} — skipping.", referenceId);
                return;
            }
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            _log.LogWarning("GrantAsync: user {UserId} not found.", userId);
            return;
        }

        user.AiCreditsRemaining += count;

        _db.AiCreditTransactions.Add(new AiCreditTransaction
        {
            UserId = userId,
            Amount = count,
            Reason = reason,
            ReferenceId = referenceId,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Granted {Count} AI credits to user {UserId} (reason={Reason}, ref={Ref}).", count, userId, reason, referenceId);
    }

    /// <inheritdoc />
    public async Task<int> GetBalanceAsync(int userId, CancellationToken ct = default)
    {
        var balance = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.AiCreditsRemaining)
            .FirstOrDefaultAsync(ct);
        return balance;
    }

    /// <inheritdoc />
    public async Task<AiCreditBalanceDto> GetBalanceWithDetailsAsync(int userId, CancellationToken ct = default)
    {
        var user = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.AiCreditsRemaining, u.HasUsedFreeAiGeneration })
            .FirstOrDefaultAsync(ct);

        if (user is null)
            return new AiCreditBalanceDto(0, false);

        return new AiCreditBalanceDto(user.AiCreditsRemaining, user.HasUsedFreeAiGeneration);
    }

    /// <inheritdoc />
    public async Task RefundGenerationChargeAsync(int? userId, string? ipAddress, AiChargeKind chargeKind, string? referenceId = null, CancellationToken ct = default)
    {
        if (chargeKind == AiChargeKind.None)
            return;

        // Idempotency guard shared by every branch below (GrantAsync already does its
        // own check for the Consumed branch, but the other two write transactions
        // directly so they need the same guard).
        if (!string.IsNullOrEmpty(referenceId)
            && chargeKind != AiChargeKind.Consumed
            && await _db.AiCreditTransactions.AsNoTracking().AnyAsync(t => t.ReferenceId == referenceId, ct))
        {
            _log.LogDebug("RefundGenerationChargeAsync: duplicate referenceId {Ref} — skipping.", referenceId);
            return;
        }

        switch (chargeKind)
        {
            case AiChargeKind.Consumed:
                if (userId is int uid)
                    await GrantAsync(uid, 1, CreditReason.Refunded, referenceId, ct);
                break;

            case AiChargeKind.FreeAuthenticated:
                if (userId is int freeUid)
                {
                    var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == freeUid, ct);
                    if (user is null)
                    {
                        _log.LogWarning("RefundGenerationChargeAsync: user {UserId} not found.", freeUid);
                        break;
                    }
                    user.HasUsedFreeAiGeneration = false;
                    _db.AiCreditTransactions.Add(new AiCreditTransaction
                    {
                        UserId = freeUid,
                        Amount = 0,
                        Reason = CreditReason.Refunded,
                        ReferenceId = referenceId,
                        CreatedAt = DateTime.UtcNow
                    });
                    await _db.SaveChangesAsync(ct);
                    _log.LogInformation("Refunded free AI generation to user {UserId} after moderation block.", freeUid);
                }
                break;

            case AiChargeKind.FreeAnonymous:
                if (!string.IsNullOrEmpty(ipAddress))
                {
                    var usage = await _db.IpAiUsages
                        .Where(u => u.IpAddress == ipAddress)
                        .OrderByDescending(u => u.CreatedAt)
                        .FirstOrDefaultAsync(ct);
                    if (usage is not null)
                        _db.IpAiUsages.Remove(usage);
                    _db.AiCreditTransactions.Add(new AiCreditTransaction
                    {
                        IpAddress = ipAddress,
                        Amount = 0,
                        Reason = CreditReason.Refunded,
                        ReferenceId = referenceId,
                        CreatedAt = DateTime.UtcNow
                    });
                    await _db.SaveChangesAsync(ct);
                    _log.LogInformation("Refunded free anonymous AI generation for IP {Ip} after moderation block.", ipAddress);
                }
                break;
        }
    }
}
