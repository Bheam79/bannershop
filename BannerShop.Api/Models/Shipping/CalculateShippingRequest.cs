using System.ComponentModel.DataAnnotations;

namespace BannerShop.Api.Models.Shipping;

public class CalculateShippingRequest
{
    [Required, StringLength(20, MinimumLength = 4)]
    public string PostalCode { get; set; } = string.Empty;

    [StringLength(100)]
    public string? City { get; set; }

    [Range(1, int.MaxValue)]
    public int BannerSizeId { get; set; }

    /// <summary>Required when the chosen banner size has IsCustomWidth = true.</summary>
    [Range(50, 1000)]
    public int? CustomWidthCm { get; set; }

    [Range(1, 1000)]
    public int Qty { get; set; } = 1;
}
