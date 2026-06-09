using BannerShop.Api.Models.DesignRequests;

namespace BannerShop.Api.Services.DesignRequests;

public interface IDesignRequestService
{
    /// <summary>
    /// Creates an AI <c>DesignRequest</c> under the BANNERSH-67 free-first flow.
    ///
    /// <para>
    /// Anonymous callers (<paramref name="userId"/> is null) are throttled by
    /// <see cref="AiCredits.IAiCreditService.IsAnonymousEligibleAsync"/> — one free
    /// generation per IP per rolling 30 days. Subsequent attempts return a 402.
    /// </para>
    /// <para>
    /// Authenticated callers consume their free generation on the first call
    /// (<see cref="Core.Entities.User.HasUsedFreeAiGeneration"/> = true) and a
    /// credit on each subsequent call. Returns 402 if no credits remain.
    /// </para>
    /// <para>No Stripe PaymentIntent is created — payment is collected later via the
    /// banner-print order's mandatory AI activation fee (BANNERSH-68).</para>
    /// </summary>
    Task<CreateAiResult> CreateAiRequestAsync(
        int? userId,
        string? ipAddress,
        CreateAiDesignRequestDto req,
        CancellationToken ct = default);

    /// <summary>
    /// Creates a Manual <c>DesignRequest</c> and a linked Draft <c>Order</c>.
    /// No Stripe PaymentIntent is created — payment is collected via the normal cart/checkout
    /// pipeline (BANNERSH-136: banner line + 495 kr designer-fee line added to cart by the
    /// frontend, then completed via the same checkout flow as custom-upload orders).
    /// </summary>
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

    /// <summary>
    /// Switches the currently active generation to a previously-generated one (BANNERSH-84).
    /// Does NOT consume a credit — only re-points the request to an existing image.
    /// </summary>
    Task<DesignRequestActionResult> ActivateGenerationAsync(int id, int generationId, int callerUserId, CancellationToken ct = default);
}

/// <summary>Result of a regenerate operation — carries the new generation id and updated credit balance.</summary>
public record RegenerateResult(
    bool Success,
    string? Error,
    int StatusCode = 200,
    int GenerationId = 0,
    int CreditsRemaining = 0,
    object? PaywallMetadata = null,
    /// <summary>
    /// Set when regeneration from an Approved/Final request created a fresh DesignRequest
    /// rather than mutating the existing one.  The frontend should switch its active
    /// design-request id to this value so polling targets the new entry.
    /// </summary>
    int? NewDesignRequestId = null)
{
    public static RegenerateResult Ok(int generationId, int creditsRemaining, int? newDesignRequestId = null)
        => new(true, null, 202, generationId, creditsRemaining, null, newDesignRequestId);

    public static RegenerateResult Fail(string error, int statusCode = 400)
        => new(false, error, statusCode);

    public static RegenerateResult Paywall(int creditsRemaining, object paywallMetadata)
        => new(false, "insufficient_credits", 402, 0, creditsRemaining, paywallMetadata);
}

/// <summary>
/// Outcome of <see cref="IDesignRequestService.CreateAiRequestAsync"/>.
///
/// <para><c>StatusCode</c> is meant to be passed straight to the HTTP response:
/// 201 on success, 402 when the caller has hit the paywall, 400 / 403 / 404 on
/// validation failures (e.g. unknown template).</para>
/// </summary>
public record CreateAiResult(
    bool Success,
    int StatusCode,
    string? Error = null,
    int DesignRequestId = 0,
    bool RequiresAuth = false,
    int CreditsRemaining = 0,
    AiPaywallResponseDto? Paywall = null)
{
    public static CreateAiResult Ok(int id, bool requiresAuth, int creditsRemaining)
        => new(true, 201, null, id, requiresAuth, creditsRemaining);

    public static CreateAiResult Fail(string error, int statusCode = 400)
        => new(false, statusCode, error);

    public static CreateAiResult PaywallResult(AiPaywallResponseDto paywall, int creditsRemaining = 0)
        => new(false, 402, paywall.Reason, 0, false, creditsRemaining, paywall);
}

public record DesignRequestActionResult(
    bool Success,
    string? Error,
    int DesignRequestId,
    string? ClientSecret = null,
    decimal TotalNok = 0m,
    DesignRequestDetailDto? Detail = null,
    decimal DesignPriceNok = 0m,
    decimal BannerPriceNok = 0m)
{
    public static DesignRequestActionResult Ok(int id, string clientSecret, decimal totalNok)
        => new(true, null, id, clientSecret, totalNok, null, totalNok, 0m);

    /// <summary>
    /// Manual-flow success with the design + banner breakdown surfaced (BANNERSH-104).
    /// <paramref name="totalNok"/> must equal <paramref name="designPriceNok"/> +
    /// <paramref name="bannerPriceNok"/> — both are echoed back to the frontend so the
    /// summary panel can render the line items without recomputing them client-side.
    /// Legacy overload — kept for compatibility; use the no-clientSecret version for new code.
    /// </summary>
    public static DesignRequestActionResult Ok(int id, string clientSecret, decimal totalNok,
        decimal designPriceNok, decimal bannerPriceNok)
        => new(true, null, id, clientSecret, totalNok, null, designPriceNok, bannerPriceNok);

    /// <summary>
    /// BANNERSH-136: Manual-flow success without a Stripe client secret.
    /// Payment is collected at checkout via the cart; the frontend adds two lines
    /// (banner + 495 kr designer fee) after this call succeeds.
    /// </summary>
    public static DesignRequestActionResult OkNoPayment(int id, decimal totalNok,
        decimal designPriceNok, decimal bannerPriceNok)
        => new(true, null, id, null, totalNok, null, designPriceNok, bannerPriceNok);

    public static DesignRequestActionResult Ok(DesignRequestDetailDto detail)
        => new(true, null, detail.Id, null, detail.PriceNok, detail, detail.PriceNok, 0m);

    public static DesignRequestActionResult Fail(string error)
        => new(false, error, 0);
}
