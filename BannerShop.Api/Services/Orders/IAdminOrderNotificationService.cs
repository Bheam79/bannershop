using BannerShop.Core.Entities;

namespace BannerShop.Api.Services.Orders;

public interface IAdminOrderNotificationService
{
    Task NotifyNewOrderAsync(Order order, CancellationToken ct = default);
}
