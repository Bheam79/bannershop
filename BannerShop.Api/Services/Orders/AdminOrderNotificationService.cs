using System.Globalization;
using System.Net;
using BannerShop.Api.Services.Email;
using BannerShop.Api.Services.SystemSettings;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BannerShop.Api.Services.Orders;

public sealed class AdminOrderNotificationOptions
{
    public string SmsEndpoint { get; set; } = "http://bheam.omnijoy.com:6969/send";
}

/// <summary>Best-effort alerts for confirmed production orders, using current admin profiles.</summary>
public sealed class AdminOrderNotificationService(
    BannerShopDbContext db,
    IEmailService email,
    IHttpClientFactory clients,
    ISystemSettingsService settings,
    IOptions<AdminOrderNotificationOptions> options,
    ILogger<AdminOrderNotificationService> log) : IAdminOrderNotificationService
{
    public const string SmsKeySetting = "admin_order_sms_key";
    public const string SmsClientName = "AdminOrderSms";

    public async Task NotifyNewOrderAsync(Order order, CancellationToken ct = default)
    {
        if (order.OrderType == OrderType.CreditPack) return;

        var admins = await db.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Admin)
            .Select(u => new { u.Id, u.Email, u.Phone })
            .ToListAsync(ct);
        if (admins.Count == 0) return;

        string? key = null;
        try { key = await settings.GetValueAsync(SmsKeySetting, ct); }
        catch (Exception ex)
        {
            log.LogError("Could not read admin SMS key for order {OrderId} ({ErrorType}).",
                order.Id, ex.GetType().Name);
        }
        if (string.IsNullOrWhiteSpace(key))
            log.LogWarning("Admin order SMS disabled: set {Setting} in /admin/settings.", SmsKeySetting);

        var amount = order.TotalNok.ToString("0.00", CultureInfo.GetCultureInfo("nb-NO"));
        var message = $"BannerShop: Ny betalt ordre #{order.Id} ({order.OrderType}), {amount} kr. Se /admin/orders/{order.Id}.";
        var body = $"<h2>Ny betalt ordre #{order.Id}</h2><p>{WebUtility.HtmlEncode(message)}</p>";

        // Each channel/recipient is isolated. A bad email or gateway outage must not
        // stop another admin's alert, nor cause Stripe to retry a completed payment.
        await Task.WhenAll(admins.Select(async admin =>
        {
            if (!string.IsNullOrWhiteSpace(admin.Email))
            {
                try { await email.SendAsync(admin.Email, $"Ny ordre – BannerShop #{order.Id}", body, ct); }
                catch (Exception ex)
                {
                    log.LogError("Admin email failed for order {OrderId}, admin {AdminId} ({ErrorType}).",
                        order.Id, admin.Id, ex.GetType().Name);
                }
            }

            var number = NormalizeNorwegianPhone(admin.Phone);
            if (number is null)
            {
                log.LogWarning("Skipping order SMS for admin {AdminId}: profile needs an 8-digit Norwegian phone number.", admin.Id);
                return;
            }
            if (string.IsNullOrWhiteSpace(key)) return;

            try
            {
                var uri = options.Value.SmsEndpoint + "?number=" + Uri.EscapeDataString(number)
                    + "&message=" + Uri.EscapeDataString(message) + "&key=" + Uri.EscapeDataString(key);
                using var client = clients.CreateClient(SmsClientName);
                using var response = await client.GetAsync(uri, ct);
                response.EnsureSuccessStatusCode();
                log.LogInformation("Admin order SMS accepted for order {OrderId}, admin {AdminId}.", order.Id, admin.Id);
            }
            catch (Exception ex)
            {
                // Never log the request URI, gateway body or exception message: the
                // provider requires the secret key and phone number in the query.
                log.LogError("Admin SMS failed for order {OrderId}, admin {AdminId} ({ErrorType}).",
                    order.Id, admin.Id, ex.GetType().Name);
            }
        }));
    }

    public static string? NormalizeNorwegianPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var compact = new string(phone.Where(c => !char.IsWhiteSpace(c) && c is not '(' and not ')' and not '-').ToArray());
        if (compact.StartsWith("+47", StringComparison.Ordinal)) compact = compact[3..];
        else if (compact.StartsWith("0047", StringComparison.Ordinal)) compact = compact[4..];
        else if (compact.Length == 10 && compact.StartsWith("47", StringComparison.Ordinal)) compact = compact[2..];
        return compact.Length == 8 && compact.All(char.IsAsciiDigit) ? "47" + compact : null;
    }
}
