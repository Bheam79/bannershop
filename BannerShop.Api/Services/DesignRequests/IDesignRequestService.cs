using BannerShop.Api.Models.DesignRequests;

namespace BannerShop.Api.Services.DesignRequests;

public interface IDesignRequestService
{
    /// <summary>Creates an AI <c>DesignRequest</c> + Stripe PaymentIntent (95 NOK).</summary>
    Task<DesignRequestActionResult> CreateAiRequestAsync(int userId, CreateAiDesignRequestDto req, CancellationToken ct = default);

    /// <summary>Lists requests owned by a single user, newest first.</summary>
    Task<IReadOnlyList<DesignRequestListItemDto>> ListMineAsync(int userId, CancellationToken ct = default);

    /// <summary>Fetch detail for a single request — returns null when not found or not owned by caller (unless admin).</summary>
    Task<DesignRequestDetailDto?> GetAsync(int id, int callerUserId, bool isAdmin, CancellationToken ct = default);

    /// <summary>Customer marks the AwaitingApproval preview as Approved.</summary>
    Task<DesignRequestActionResult> ApproveAsync(int id, int callerUserId, CancellationToken ct = default);

    /// <summary>
    /// Called after a successful Stripe payment for this request: flips status Pending→InProgress
    /// and enqueues the AI generation job. Idempotent for repeated webhook deliveries.
    /// </summary>
    Task MarkPaidAndEnqueueAsync(string paymentIntentId, CancellationToken ct = default);
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
