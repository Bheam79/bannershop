using BannerShop.Core.Entities;
using BannerShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Services.Orders;

/// <summary>
/// Shared EF Core loaders used by both <see cref="OrderService"/> (customer + webhook)
/// and <see cref="AdminOrderService"/>. Extracted from <c>OrderService</c> as part of
/// BANNERSH-199 so the two service classes don't redeclare the same eager-loading
/// graph.
/// </summary>
internal static class OrderQueries
{
    /// <summary>
    /// Loads an order with all eager-include navigations the DTO mappers depend on
    /// (User, ShippingAddress, ShipmentTracking, Items → BannerSize → Material,
    /// Items → BannerDesign, Items → ProductionStatuses).
    /// </summary>
    public static Task<Order?> LoadFullOrderAsync(BannerShopDbContext db, int orderId, CancellationToken ct)
        => db.Orders
            .Include(o => o.User)
            .Include(o => o.ShippingAddress)
            .Include(o => o.ShipmentTracking)
            .Include(o => o.Items).ThenInclude(i => i.BannerSize).ThenInclude(s => s!.Material)
            .Include(o => o.Items).ThenInclude(i => i.BannerDesign)
            .Include(o => o.Items).ThenInclude(i => i.ProductionStatuses)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

    /// <summary>
    /// Returns the single DesignRequest linked to this order (if any), loading via
    /// the <c>DesignRequest.OrderId</c> FK. Returns null for CustomBanner orders or
    /// when no DesignRequest has been linked yet.
    /// </summary>
    public static Task<DesignRequest?> LoadDesignRequestForOrderAsync(BannerShopDbContext db, int orderId, CancellationToken ct)
        => db.DesignRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.OrderId == orderId, ct);

    /// <summary>
    /// Batch-loads DesignRequests for a set of order IDs, keyed by OrderId.
    /// Orders with no linked DesignRequest will have no entry in the result dictionary.
    /// </summary>
    public static async Task<Dictionary<int, DesignRequest>> LoadDesignRequestsForOrdersAsync(
        BannerShopDbContext db, IReadOnlyList<int> orderIds, CancellationToken ct)
    {
        if (orderIds.Count == 0) return new Dictionary<int, DesignRequest>();
        var list = await db.DesignRequests.AsNoTracking()
            .Where(r => r.OrderId.HasValue && orderIds.Contains(r.OrderId!.Value))
            .ToListAsync(ct);
        // In the unlikely case of multiple DesignRequests per order, keep the first one.
        return list
            .GroupBy(r => r.OrderId!.Value)
            .ToDictionary(g => g.Key, g => g.First());
    }
}
