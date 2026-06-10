using BannerShop.Api.Models.Shipping;
using BannerShop.Api.Services;
using BannerShop.Api.Services.Shipping;
using BannerShop.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ShippingController : ControllerBase
{
    private readonly BannerShopDbContext _db;
    private readonly IShippingService _shipping;
    private readonly ParcelCalculator _parcels;
    private readonly ILogger<ShippingController> _logger;

    public ShippingController(
        BannerShopDbContext db,
        IShippingService shipping,
        ParcelCalculator parcels,
        ILogger<ShippingController> logger)
    {
        _db = db;
        _shipping = shipping;
        _parcels = parcels;
        _logger = logger;
    }

    // ── POST /api/shipping/calculate ──────────────────────────────────────────
    [HttpPost("calculate")]
    public async Task<IActionResult> Calculate([FromBody] CalculateShippingRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var size = await _db.BannerSizes
            .Include(s => s.Material)
            .FirstOrDefaultAsync(s => s.Id == req.BannerSizeId, ct);

        if (size is null)
            return NotFound(new { error = $"Banner size {req.BannerSizeId} not found." });

        if (size.IsCustomWidth && req.CustomWidthCm is null)
            return BadRequest(new { error = "customWidthCm is required for custom-width banner sizes." });

        ParcelDimensions parcel;
        try
        {
            parcel = await _parcels.CalculateAsync(size, req.CustomWidthCm, req.Qty, req.PackingMode, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        try
        {
            var quote = await _shipping.CalculateAsync(req.PostalCode, req.City, parcel, ct);
            return Ok(new ShippingCalculationResponse
            {
                Standard = ToDto(quote.Standard),
                Express  = ToDto(quote.Express),
                Parcel   = new ParcelDimensionsDto
                {
                    LengthCm = parcel.LengthCm,
                    WidthCm  = parcel.WidthCm,
                    HeightCm = parcel.HeightCm,
                    WeightKg = parcel.WeightKg
                }
            });
        }
        catch (ShippingUnavailableException ex)
        {
            _logger.LogWarning(ex, "Shipping unavailable for {PostalCode}", req.PostalCode);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = "Shipping cost is temporarily unavailable. Please contact the shop to complete your order.",
                detail = ex.Message
            });
        }
    }

    // ── POST /api/shipping/parcel-preview ─────────────────────────────────────
    // BANNERSH-180: lightweight endpoint that returns the parcel dimensions +
    // weight a banner would be shipped as for a given packing mode, without
    // calling the carrier API or requiring a postal code. Used by the checkout
    // UI to show "what we'll send to Bring" under each Pakkemetode option.
    [HttpPost("parcel-preview")]
    public async Task<IActionResult> ParcelPreview([FromBody] ParcelPreviewRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var size = await _db.BannerSizes
            .Include(s => s.Material)
            .FirstOrDefaultAsync(s => s.Id == req.BannerSizeId, ct);

        if (size is null)
            return NotFound(new { error = $"Banner size {req.BannerSizeId} not found." });

        if (size.IsCustomWidth && req.CustomWidthCm is null)
            return BadRequest(new { error = "customWidthCm is required for custom-width banner sizes." });

        ParcelDimensions parcel;
        try
        {
            parcel = await _parcels.CalculateAsync(size, req.CustomWidthCm, req.Qty, req.PackingMode, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return Ok(new ParcelDimensionsDto
        {
            LengthCm = parcel.LengthCm,
            WidthCm  = parcel.WidthCm,
            HeightCm = parcel.HeightCm,
            WeightKg = parcel.WeightKg
        });
    }

    private static ShippingOptionDto ToDto(ShippingOption o) => new()
    {
        Cost = o.CostNok,
        EstimatedDays = o.EstimatedDays,
        CarrierProductId = o.CarrierProductId,
        CarrierProductName = o.CarrierProductName
    };
}
