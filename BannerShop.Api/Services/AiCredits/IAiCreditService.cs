using BannerShop.Core.Enums;

namespace BannerShop.Api.Services.AiCredits;

/// <summary>
/// Manages the AI generation credit pool and IP-based anonymous throttling.
/// </summary>
public interface IAiCreditService
{
    /// <summary>
    /// Returns true if the given IP address is eligible for a free anonymous AI generation
    /// (i.e. it has not already used one within the rolling 30-day window).
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
}

/// <summary>Response DTO for GET /api/ai-credits/me.</summary>
public record AiCreditBalanceDto(int CreditsRemaining, bool HasUsedFreeGeneration);
