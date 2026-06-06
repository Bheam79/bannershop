namespace BannerShop.Core.Entities;

public class PricingParameter
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public string? Description { get; set; }
}
