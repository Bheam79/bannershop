using BannerShop.Api.Models.DesignRequests;

namespace BannerShop.Api.Services.DesignRequests;

public interface IDesignRequestService
{
    /// <summary>Creates an AI <c>DesignRequest</c> + Stripe PaymentIntent (95 NOK).</summary>
    Task<DesignRequestActionResult> CreateAiRequestAsync(int userId, CreateAiDesignRequestDto req, CancellationToken ct = default);

    /// <summary>Creates a Manual <c>DesignRequest</c> + Stripe PaymentIntent (495 NOK).</summary>
    Task<DesignRequestActionResult> CreateManualRequestAsync(int userId, CreateManualDesignRequestDto req, CancellationToken ct = default);

    /// <summary>Submits a customer revision comment (Manual flow only, max 1 free revision).</summary>
    Task<DesignRequestActionResult> RequestRevisionAsync(int id, int callerUserId, string comment, CancellationToken ct = default);

    /// <summary>Lists requests owned by a single user, newest first.</summary>
    Task<IReadOnlyList<DesignRequestListItemDto>> ListMineAsync(int userId, CancellationToken ct = default);

    /// <summary>Fetch detail for a single request — returns null when not found or not owned by caller (unless admin).</summary>
    Task<DesignRequestDetailDto?> GetAsync(int id, int callerUserId, bool isAdmin, CancellationToken ct = default);

    /// <summary>Customer marks the AwaitingApproval preview as Approved.</summary>
    Task<DesignRequestActionResult> ApproveAsync(int id, int callerUserId, CancellationToken ct = default);

    /// <summary>
    /// Called after a successful Stripe payment: flips status Pending→InProgress.
    /// For AI mode also enqueues the generation job.
    /// Idempotent for repeated webhook deliveries.
    /// </summary>
    Task MarkPaidAndEnqueueAsync(string paymentIntentId, CancellationToken ct = default);

    /// <summary>
    /// Regenerates an AI design by consuming 1 credit, optionally updating mutable inputs,
    /// resetting status to InProgress, and enqueuing a new generation job.
    /// Returns 402-style failure when the user has no credits.
    /// </summary>
    Task<RegenerateResult> RegenerateAsync(int id, int callerUserId, RegenerateAiRequestDto req, CancellationToken ct = default);
}

/// <summary>Result of a regenerate operation — carries the new generation id and updated credit balance.</summary>
public record RegenerateResult(
    bool Success,
    string? Error,
    int StatusCode = 200,
    int GenerationId = 0,
    int CreditsRemaining = 0,
    object? PaywallMetadata = null)
{
    public static RegenerateResult Ok(int generationId, int creditsRemaining)
        => new(true, null, 202, generationId, creditsRemaining);

    public static RegenerateResult Fail(string error, int statusCode = 400)
        => new(false, error, statusCode);

    public static RegenerateResult Paywall(int creditsRemaining, object paywallMetadata)
        => new(false, "insufficient_credits", 402, 0, creditsRemaining, paywallMetadata);
}

public record DesignRequestActionResult(
    bool Success,
    string? Error,
    int DesignRequestId,
    string? ClientSecret = null,
    decimal TotalNok = 0m,
    DesignRequestDetailDto? Detail = null)
{
    public static DesignRequestActionResult Ok(int id, string clientSecret, decimal totalNok)
        => new(true, null, id, clientSecret, totalNok);

    public static DesignRequestActionResult Ok(DesignRequestDetailDto detail)
        => new(true, null, detail.Id, null, detail.PriceNok, detail);

    public static DesignRequestActionResult Fail(string error)
        => new(false, error, 0);
}
