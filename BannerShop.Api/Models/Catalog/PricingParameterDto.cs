namespace BannerShop.Api.Models.Catalog;

public class PricingParameterDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public string? Description { get; set; }
}
