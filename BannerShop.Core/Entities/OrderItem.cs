using BannerShop.Core.Enums;

namespace BannerShop.Core.Entities;

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int? BannerSizeId { get; set; }
    public int? CustomWidthCm { get; set; }
    public int HeightCm { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal AreaSqm { get; set; }

    /// <summary>Base banner price per unit (excluding eyelet addon).</summary>
    public decimal UnitPriceNok { get; set; }

    /// <summary>
    /// Eyelet (malje) finishing option chosen by the customer.
    /// Hem is not possible on PVC banners.
    /// </summary>
    public EyeletOption EyeletOption { get; set; } = EyeletOption.None;

    /// <summary>Number of eyelets on this banner (snapshotted at order time).</summary>
    public int EyeletCount { get; set; } = 0;

    /// <summary>
    /// Total eyelet fee per single banner (EyeletCount × price_per_eyelet),
    /// snapshotted at order time. Excluded from <see cref="UnitPriceNok"/>
    /// so the banner price and the finishing option are always visible separately.
    /// </summary>
    public decimal EyeletFeeNok { get; set; } = 0m;

    /// <summary>
    /// Total line cost: (<see cref="UnitPriceNok"/> + <see cref="EyeletFeeNok"/>) × <see cref="Quantity"/>.
    /// </summary>
    public decimal LineTotalNok { get; set; }

    public string? Notes { get; set; }
    public int? BannerDesignId { get; set; }

    /// <summary>
    /// FK to the <see cref="DesignRequest"/> when this item is an AI-designed banner.
    /// Used to validate ownership during order creation and to trigger the AI activation
    /// fee (BANNERSH-68). Null for self-uploaded designs.
    /// </summary>
    public int? DesignRequestId { get; set; }

    // Navigation
    public Order Order { get; set; } = null!;
    public BannerSize? BannerSize { get; set; }
    public BannerDesign? BannerDesign { get; set; }
    public DesignRequest? DesignRequest { get; set; }
    public ICollection<ProductionStatus> ProductionStatuses { get; set; } = new List<ProductionStatus>();
}
