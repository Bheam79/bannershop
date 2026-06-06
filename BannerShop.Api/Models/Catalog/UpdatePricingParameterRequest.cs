using System.ComponentModel.DataAnnotations;

namespace BannerShop.Api.Models.Catalog;

public class UpdatePricingParameterRequest
{
    [Required]
    public decimal Value { get; set; }
}
