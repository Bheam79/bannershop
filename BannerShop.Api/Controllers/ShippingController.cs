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

        var material = await _db.Materials.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == req.MaterialId, ct);
        if (material is null)
            return NotFound(new { error = $"Material {req.MaterialId} not found." });

        IReadOnlyList<ParcelDimensions> packages;
        try
        {
            // BANNERSH-274: split qty into packages of ≤ MaxItemsPerPackage and
            // quote each package separately; the total shipping cost is the sum.
            packages = await _parcels.SplitIntoPackagesAsync(
                req.WidthCm, req.HeightCm, material.WeightGsm, req.Qty, req.PackingMode, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        try
        {
            decimal totalStandardCost = 0m;
            decimal totalExpressCost  = 0m;
            int maxDays = 0;
            string? productId   = null;
            string? productName = null;

            foreach (var parcel in packages)
            {
                var quote = await _shipping.CalculateAsync(req.PostalCode, req.City, parcel, ct);
                totalStandardCost += quote.Standard.CostNok;
                totalExpressCost  += quote.Express.CostNok;
                if (quote.Standard.EstimatedDays > maxDays) maxDays = quote.Standard.EstimatedDays;
                // Use product info from first successful quote
                productId   ??= quote.Standard.CarrierProductId;
                productName ??= quote.Standard.CarrierProductName;
            }

            // Representative parcel dims for the UI (first package)
            var repr = packages[0];
            return Ok(new ShippingCalculationResponse
            {
                Standard = new ShippingOptionDto
                {
                    Cost               = totalStandardCost,
                    EstimatedDays      = maxDays,
                    CarrierProductId   = productId,
                    CarrierProductName = productName,
                },
                Express = new ShippingOptionDto
                {
                    Cost               = totalExpressCost,
                    EstimatedDays      = maxDays,
                    CarrierProductId   = productId,
                    CarrierProductName = productName,
                },
                Parcel = new ParcelDimensionsDto
                {
                    LengthCm     = repr.LengthCm,
                    WidthCm      = repr.WidthCm,
                    HeightCm     = repr.HeightCm,
                    WeightKg     = repr.WeightKg,
                    PackageCount = packages.Count,
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
    [HttpPost("parcel-preview")]
    public async Task<IActionResult> ParcelPreview([FromBody] ParcelPreviewRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var material = await _db.Materials.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == req.MaterialId, ct);
        if (material is null)
            return NotFound(new { error = $"Material {req.MaterialId} not found." });

        IReadOnlyList<ParcelDimensions> packages;
        try
        {
            // BANNERSH-274: split into packages of ≤ MaxItemsPerPackage; return
            // representative (first) package dims + the total package count.
            packages = await _parcels.SplitIntoPackagesAsync(
                req.WidthCm, req.HeightCm, material.WeightGsm, req.Qty, req.PackingMode, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var repr = packages[0];
        return Ok(new ParcelDimensionsDto
        {
            LengthCm     = repr.LengthCm,
            WidthCm      = repr.WidthCm,
            HeightCm     = repr.HeightCm,
            WeightKg     = repr.WeightKg,
            PackageCount = packages.Count,
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
