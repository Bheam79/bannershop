namespace BannerShop.Api.Models.Catalog;

public class PriceResponseDto
{
    public int SizeId { get; set; }
    public int? CustomWidthCm { get; set; }
    public decimal PriceNok { get; set; }
}
