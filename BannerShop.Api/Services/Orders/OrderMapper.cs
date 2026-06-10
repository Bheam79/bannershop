using BannerShop.Api.Models.Orders;
using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;

namespace BannerShop.Api.Services.Orders;

/// <summary>
/// Static DTO mappers for <see cref="Order"/> and its sub-records. Pure data-shape
/// transformations with no side-effects (similar to an AutoMapper profile, but
/// without the framework).
/// </summary>
/// <remarks>
/// Extracted from <c>OrderService</c> as part of BANNERSH-199 so the
/// customer/admin service classes can stay focused on orchestration and the
/// mapping rules live in a single discoverable place. Callers pass the
/// <see cref="BannerFileStorage"/> instance they already own — the mapper
/// uses it to resolve preview-image public URLs.
/// </remarks>
internal static class OrderMapper
{
    /// <summary>Builds the list-item DTO from a loaded Order entity + optional linked DesignRequest.</summary>
    public static OrderListItemDto ToListItemDto(Order o, DesignRequest? dr, BannerFileStorage storage)
    {
        var (customBanner, aiBanner, manualDesign) = BuildTypeSpecificDetails(o, dr, storage);
        return new OrderListItemDto
        {
            Id = o.Id,
            Status = o.Status.ToString(),
            OrderType = o.OrderType.ToString(),
            OrderState = o.OrderState.ToString(),
            DeliveryType = o.DeliveryType.ToString(),
            PackingMode = o.PackingMode.ToString(),
            TotalNok = o.TotalNok,
            ItemCount = o.Items.Count,
            CreatedAt = o.CreatedAt,
            EstimatedDelivery = o.EstimatedDelivery,
            CustomerName = o.User?.Name,
            CustomerEmail = o.User?.Email,
            CustomBanner = customBanner,
            AiBanner = aiBanner,
            ManualDesign = manualDesign
        };
    }

    /// <summary>Builds the full detail DTO from a loaded Order entity + optional linked DesignRequest.</summary>
    public static OrderDetailDto ToDetailDto(Order o, DesignRequest? dr, BannerFileStorage storage)
    {
        var (customBanner, aiBanner, manualDesign) = BuildTypeSpecificDetails(o, dr, storage);
        return new OrderDetailDto
        {
            Id = o.Id,
            UserId = o.UserId,
            CustomerName = o.User?.Name,
            CustomerEmail = o.User?.Email,
            Status = o.Status.ToString(),
            OrderType = o.OrderType.ToString(),
            OrderState = o.OrderState.ToString(),
            DeliveryType = o.DeliveryType.ToString(),
            PackingMode = o.PackingMode.ToString(),
            ShippingCostNok = o.ShippingCostNok,
            ExpressFeeNok = o.ExpressFeeNok,
            AiActivationFeeNok = o.AiActivationFeeNok,
            TotalNok = o.TotalNok,
            StripePaymentIntentId = o.StripePaymentIntentId,
            CreatedAt = o.CreatedAt,
            UpdatedAt = o.UpdatedAt,
            EstimatedDelivery = o.EstimatedDelivery,
            ShippingAddress = o.ShippingAddress is null ? null : new OrderAddressDto
            {
                Line1 = o.ShippingAddress.Line1,
                Line2 = o.ShippingAddress.Line2,
                PostalCode = o.ShippingAddress.PostalCode,
                City = o.ShippingAddress.City,
                Country = o.ShippingAddress.Country
            },
            Items = o.Items.OrderBy(i => i.Id).Select(i => new OrderItemDto
            {
                Id = i.Id,
                BannerSizeId = i.BannerSizeId,
                BannerSizeName = i.BannerSize?.Name,
                CustomWidthCm = i.CustomWidthCm,
                HeightCm = i.HeightCm,
                Quantity = i.Quantity,
                AreaSqm = i.AreaSqm,
                UnitPriceNok = i.UnitPriceNok,
                EyeletOption = i.EyeletOption.ToString(),
                EyeletCount = i.EyeletCount,
                EyeletFeeNok = i.EyeletFeeNok,
                LineTotalNok = i.LineTotalNok,
                Notes = i.Notes,
                BannerDesignId = i.BannerDesignId,
                DesignRequestId = i.DesignRequestId,
                CurrentProductionStage = (i.ProductionStatuses.OrderByDescending(p => p.UpdatedAt).FirstOrDefault()?.Stage
                                          ?? ProductionStage.Queued).ToString(),
                ProductionStatusHistory = i.ProductionStatuses
                    .OrderBy(p => p.UpdatedAt)
                    .Select(p => new ProductionStatusDto
                    {
                        Id = p.Id,
                        Stage = p.Stage.ToString(),
                        UpdatedAt = p.UpdatedAt,
                        Notes = p.Notes
                    }).ToList()
            }).ToList(),
            ShipmentTracking = o.ShipmentTracking is null ? null : new ShipmentTrackingDto
            {
                Carrier = o.ShipmentTracking.Carrier,
                TrackingNumber = o.ShipmentTracking.TrackingNumber,
                TrackingUrl = o.ShipmentTracking.TrackingUrl,
                ShippedAt = o.ShipmentTracking.ShippedAt,
                EstimatedArrival = o.ShipmentTracking.EstimatedArrival,
                DeliveredAt = o.ShipmentTracking.DeliveredAt
            },
            CustomBanner = customBanner,
            AiBanner = aiBanner,
            ManualDesign = manualDesign
        };
    }

    /// <summary>
    /// Builds the three type-specific detail sub-objects from the loaded Order and its
    /// linked DesignRequest (when present). Exactly one of the three returned values
    /// is non-null, matching the order's <see cref="OrderType"/>.
    /// </summary>
    private static (CustomBannerDetailDto?, AiBannerDetailDto?, ManualDesignDetailDto?) BuildTypeSpecificDetails(
        Order o, DesignRequest? dr, BannerFileStorage storage)
    {
        switch (o.OrderType)
        {
            case OrderType.CustomBanner:
            {
                var firstItem = o.Items.OrderBy(i => i.Id).FirstOrDefault();
                var previewPath = firstItem?.BannerDesign?.PreviewStoragePath
                               ?? firstItem?.BannerDesign?.StoragePath;
                return (new CustomBannerDetailDto
                {
                    PreviewUrl = previewPath is null ? null : storage.PublicUrlFor(previewPath),
                    BannerSizeName = firstItem?.BannerSize?.Name,
                    MaterialName = firstItem?.BannerSize?.Material?.Name
                }, null, null);
            }
            case OrderType.AiBanner:
            {
                if (dr is null) return (null, new AiBannerDetailDto(), null);
                var previewPath = dr.AiPreviewPath
                               ?? dr.FinalCroppedStoragePath
                               ?? dr.AiResultStoragePath;
                return (null, new AiBannerDetailDto
                {
                    PreviewUrl = previewPath is null ? null : storage.PublicUrlFor(previewPath),
                    ThemeDescription = dr.ThemeDescription,
                    PersonName = dr.PersonName,
                    RevisionCount = dr.RevisionCount,
                    DesignRequestId = dr.Id
                }, null);
            }
            case OrderType.ManualDesign:
            {
                if (dr is null) return (null, null, new ManualDesignDetailDto());
                var previewPath = dr.DesignerPreviewPath ?? dr.FinalCroppedStoragePath;
                return (null, null, new ManualDesignDetailDto
                {
                    PreviewUrl = previewPath is null ? null : storage.PublicUrlFor(previewPath),
                    AspectRatio = dr.AspectRatio,
                    DesignerNotes = dr.DesignerNotes,
                    DesignRequestId = dr.Id
                });
            }
            default:
                return (null, null, null);
        }
    }
}
