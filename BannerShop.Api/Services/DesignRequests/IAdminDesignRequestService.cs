using BannerShop.Api.Models.DesignRequests;

namespace BannerShop.Api.Services.DesignRequests;

public interface IAdminDesignRequestService
{
    /// <summary>Paginated list of all design requests, optionally filtered.</summary>
    Task<PagedResult<AdminDesignRequestListItemDto>> ListAsync(AdminDesignRequestFilter filter, CancellationToken ct = default);

    /// <summary>Full admin detail including customer info, photo URL, and revision history.</summary>
    Task<AdminDesignRequestDetailDto?> GetDetailAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Admin saves a designer preview image and moves the request to AwaitingApproval.
    /// Optionally sends an email notification to the customer.
    /// </summary>
    Task<DesignRequestActionResult> UploadPreviewAsync(int id, string storagePath, CancellationToken ct = default);

    /// <summary>Admin manually overrides the request status (InProgress, AwaitingApproval, Final, Cancelled).</summary>
    Task<DesignRequestActionResult> UpdateStatusAsync(int id, string status, string? notes, CancellationToken ct = default);

    /// <summary>
    /// Run the design request's final cropped image (or raw AI output when no cropped
    /// version exists yet) through the configured 4x upscaler (Real-ESRGAN via Replicate
    /// — see BANNERSH-57) and replace <see cref="DesignRequest.FinalCroppedStoragePath"/>
    /// with the upscaled file. Used by the order backend to prep print-ready assets.
    /// </summary>
    Task<DesignRequestActionResult> UpscaleFinalAsync(int id, int scale, CancellationToken ct = default);
}
