namespace BannerShop.Api.Models.Shipping;

public class ShippingOptionDto
{
    public decimal Cost { get; set; }
    public int EstimatedDays { get; set; }
    public string? CarrierProductId { get; set; }
    public string? CarrierProductName { get; set; }
}

public class ShippingCalculationResponse
{
    public ShippingOptionDto Standard { get; set; } = new();
    public ShippingOptionDto Express  { get; set; } = new();

    /// <summary>Echoed parcel dimensions used for the quote (handy for UI debugging).</summary>
    public ParcelDimensionsDto Parcel { get; set; } = new();
}

public class ParcelDimensionsDto
{
    public decimal LengthCm { get; set; }
    public decimal WidthCm  { get; set; }
    public decimal HeightCm { get; set; }
    public decimal WeightKg { get; set; }
}
