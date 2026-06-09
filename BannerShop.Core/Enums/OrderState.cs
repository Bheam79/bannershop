namespace BannerShop.Core.Enums;

/// <summary>
/// Full superset of lifecycle states across all order types.
/// Not all states are reachable for every <see cref="OrderType"/> — use
/// <see cref="BannerShop.Core.Helpers.OrderStateHelper.ValidSequence"/> to obtain
/// the applicable sequence for a given order type.
/// Stored as a tinyint in the DB.
/// </summary>
public enum OrderState : byte
{
    /// <summary>Order created but not yet paid.</summary>
    Draft = 0,

    /// <summary>Payment confirmed.</summary>
    Paid = 1,

    /// <summary>Designer has uploaded the final artwork (ManualDesign only).</summary>
    DesignReady = 2,

    /// <summary>Customer must review and approve before printing (AI + ManualDesign).</summary>
    CustomerApproval = 3,

    /// <summary>Banner is being printed / produced.</summary>
    InProduction = 4,

    /// <summary>Package handed to carrier.</summary>
    Shipped = 5,

    /// <summary>Package confirmed delivered to customer.</summary>
    Delivered = 6
}
