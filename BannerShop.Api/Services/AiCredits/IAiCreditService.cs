using BannerShop.Core.Enums;

namespace BannerShop.Api.Services.AiCredits;

/// <summary>
/// Manages the AI generation credit pool and IP-based anonymous throttling.
/// </summary>
public interface IAiCreditService
{
    /// <summary>
    /// Returns true if the given IP address is eligible for a free anonymous AI generation
    /// (i.e. it has used fewer than 2 within the rolling 30-day window).
    /// </summary>
    Task<bool> IsAnonymousEligibleAsync(string ipAddress, CancellationToken ct = default);

    /// <summary>
    /// Records an anonymous AI generation for the given IP address.
    /// Call this immediately after the pipeline is enqueued, not before eligibility is checked.
    /// </summary>
    Task RecordAnonymousUsageAsync(string ipAddress, CancellationToken ct = default);

    /// <summary>
    /// Attempts to deduct <paramref name="count"/> credits from <paramref name="userId"/>'s pool.
    /// Returns false (without modifying the DB) if the user has insufficient credits.
    /// </summary>
    Task<bool> TryConsumeAsync(int userId, int count = 1, CancellationToken ct = default);

    /// <summary>
    /// Grants <paramref name="count"/> credits to <paramref name="userId"/>.
    /// Idempotent when <paramref name="referenceId"/> is provided — a second call with the
    /// same <paramref name="referenceId"/> is a no-op.
    /// </summary>
    Task GrantAsync(int userId, int count, CreditReason reason, string? referenceId = null, CancellationToken ct = default);

    /// <summary>Returns the current credit balance for <paramref name="userId"/>.</summary>
    Task<int> GetBalanceAsync(int userId, CancellationToken ct = default);

    /// <summary>Returns balance + free-generation status for the /api/ai-credits/me endpoint.</summary>
    Task<AiCreditBalanceDto> GetBalanceWithDetailsAsync(int userId, CancellationToken ct = default);

    /// <summary>
    /// Reverses whatever was charged for a generation attempt that the image provider's moderation
    /// blocked (BANNERSH-288), so a moderated request never costs the customer anything:
    /// <see cref="AiChargeKind.Consumed"/> grants back 1 credit, <see cref="AiChargeKind.FreeAuthenticated"/>
    /// resets <c>User.HasUsedFreeAiGeneration</c> so the free try can be used again, and
    /// <see cref="AiChargeKind.FreeAnonymous"/> removes the IP's usage record so the
    /// rolling-window eligibility check isn't affected. A no-op for <see cref="AiChargeKind.None"/>.
    /// Idempotent when <paramref name="referenceId"/> is provided.
    /// </summary>
    Task RefundGenerationChargeAsync(int? userId, string? ipAddress, AiChargeKind chargeKind, string? referenceId = null, CancellationToken ct = default);
}

/// <summary>Response DTO for GET /api/ai-credits/me.</summary>
public record AiCreditBalanceDto(int CreditsRemaining, bool HasUsedFreeGeneration);
