using System.Globalization;
using System.Net;
using System.Text;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;

namespace BannerShop.Api.Services.Email;

/// <summary>
/// Builds HTML bodies for transactional order emails.
/// All methods are pure/static so they are easy to test and locate.
/// </summary>
public static class OrderEmailTemplates
{
    /// <summary>Norwegian (nb-NO) culture for currency/date formatting in customer mail.</summary>
    private static readonly CultureInfo NoCulture = CultureInfo.GetCultureInfo("nb-NO");

    public static string BuildOrderConfirmationHtml(Order o)
    {
        var customerName = string.IsNullOrWhiteSpace(o.User?.Name) ? "kunde" : o.User!.Name;
        var itemsSubtotal = o.Items.Sum(i => i.LineTotalNok);
        var estimatedDelivery = o.EstimatedDelivery.HasValue
            ? o.EstimatedDelivery.Value.ToString("d. MMMM yyyy", NoCulture)
            : "ikke fastsatt";

        var sb = new StringBuilder();
        sb.Append("<html><body style=\"font-family:Arial,Helvetica,sans-serif;color:#222;\">");
        sb.Append($"<p>Hei {Esc(customerName)},</p>");
        sb.Append($"<p>Takk for bestillingen din! Vi har mottatt betaling for ordre <strong>#{o.Id}</strong>.</p>");
        sb.Append("<h3>Bestilte varer</h3>");
        sb.Append("<table cellpadding=\"6\" cellspacing=\"0\" border=\"1\" style=\"border-collapse:collapse;border-color:#ccc;\">");
        sb.Append("<thead><tr style=\"background:#f5f5f5;text-align:left;\">");
        sb.Append("<th>Bannerstørrelse</th><th>Antall</th><th>Enhetspris</th><th>Sum</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var item in o.Items.OrderBy(i => i.Id))
        {
            var sizeName = item.BannerSize?.Name ?? $"Bannerstørrelse {item.BannerSizeId}";
            var widthCm = item.CustomWidthCm ?? item.BannerSize?.WidthCm;
            var dims = widthCm.HasValue
                ? $"{widthCm}×{item.HeightCm} cm"
                : $"{item.HeightCm} cm høyde";
            sb.Append("<tr>");
            sb.Append($"<td>{Esc(sizeName)} <span style=\"color:#666;\">({dims})</span></td>");
            sb.Append($"<td>{item.Quantity}</td>");
            sb.Append($"<td>{FormatNok(item.UnitPriceNok)}</td>");
            sb.Append($"<td>{FormatNok(item.LineTotalNok)}</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");

        sb.Append("<h3>Sammendrag</h3>");
        sb.Append("<table cellpadding=\"4\" cellspacing=\"0\" border=\"0\">");
        sb.Append($"<tr><td>Delsum varer</td><td style=\"text-align:right;\">{FormatNok(itemsSubtotal)}</td></tr>");
        if (o.DeliveryType == DeliveryType.Pickup)
            sb.Append("<tr><td>Levering</td><td style=\"text-align:right;\">Henting (gratis)</td></tr>");
        else
            sb.Append($"<tr><td>Frakt</td><td style=\"text-align:right;\">{FormatNok(o.ShippingCostNok)}</td></tr>");
        if (o.ExpressFeeNok > 0m)
            sb.Append($"<tr><td>Ekspressgebyr</td><td style=\"text-align:right;\">{FormatNok(o.ExpressFeeNok)}</td></tr>");
        if (o.AiActivationFeeNok > 0m)
            sb.Append($"<tr><td>AI aktivering</td><td style=\"text-align:right;\">{FormatNok(o.AiActivationFeeNok)}</td></tr>");
        sb.Append($"<tr><td><strong>Totalsum</strong></td><td style=\"text-align:right;\"><strong>{FormatNok(o.TotalNok)}</strong></td></tr>");
        sb.Append("</table>");

        if (o.DeliveryType == DeliveryType.Pickup)
        {
            sb.Append("<p>Bestillingen kan hentes i <strong>Rigedalen 43, 4626 Kristiansand</strong> mellom kl. 09–15 ukedager, eller etter avtale.</p>");
            sb.Append("<p>Vi tar kontakt når bestillingen er klar for henting.</p>");
        }
        else
        {
            sb.Append($"<p>Estimert leveringsdato: <strong>{Esc(estimatedDelivery)}</strong>.</p>");
            sb.Append("<p>Vi gir beskjed igjen så snart pakken er sendt fra oss.</p>");
        }
        sb.Append("<p>Vennlig hilsen,<br/>BannerShop</p>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    public static string BuildProductionStartedHtml(Order o)
    {
        var customerName = string.IsNullOrWhiteSpace(o.User?.Name) ? "kunde" : o.User!.Name;
        var estimatedDelivery = o.EstimatedDelivery.HasValue
            ? o.EstimatedDelivery.Value.ToString("d. MMMM yyyy", NoCulture)
            : "ikke fastsatt";

        var sb = new StringBuilder();
        sb.Append("<html><body style=\"font-family:Arial,Helvetica,sans-serif;color:#222;\">");
        sb.Append($"<p>Hei {Esc(customerName)},</p>");
        sb.Append($"<p>Bestillingen din <strong>#{o.Id}</strong> er nå sendt til produksjon. Vi er i gang med å trykke banneret ditt!</p>");
        sb.Append($"<p>Estimert leveringsdato: <strong>{Esc(estimatedDelivery)}</strong>.</p>");
        sb.Append("<p>Du vil motta en ny melding når pakken er sendt fra oss.</p>");
        sb.Append("<p>Vennlig hilsen,<br/>BannerShop</p>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    public static string BuildShipmentDispatchedHtml(Order o)
    {
        var customerName = string.IsNullOrWhiteSpace(o.User?.Name) ? "kunde" : o.User!.Name;
        var t = o.ShipmentTracking!;
        var arrival = t.EstimatedArrival?.ToString("d. MMMM yyyy", NoCulture)
                      ?? o.EstimatedDelivery?.ToString("d. MMMM yyyy", NoCulture)
                      ?? "ikke fastsatt";

        var sb = new StringBuilder();
        sb.Append("<html><body style=\"font-family:Arial,Helvetica,sans-serif;color:#222;\">");
        sb.Append($"<p>Hei {Esc(customerName)},</p>");
        sb.Append($"<p>Gode nyheter — ordre <strong>#{o.Id}</strong> er nå sendt fra oss.</p>");
        sb.Append("<h3>Sporing</h3>");
        sb.Append("<table cellpadding=\"4\" cellspacing=\"0\" border=\"0\">");
        sb.Append($"<tr><td>Transportør</td><td><strong>{Esc(t.Carrier)}</strong></td></tr>");
        sb.Append($"<tr><td>Sporingsnummer</td><td><strong>{Esc(t.TrackingNumber)}</strong></td></tr>");
        if (!string.IsNullOrWhiteSpace(t.TrackingUrl))
            sb.Append($"<tr><td>Sporing</td><td><a href=\"{Esc(t.TrackingUrl)}\">Følg pakken</a></td></tr>");
        sb.Append($"<tr><td>Estimert ankomst</td><td>{Esc(arrival)}</td></tr>");
        sb.Append("</table>");
        sb.Append("<p>Takk for at du handlet hos oss!</p>");
        sb.Append("<p>Vennlig hilsen,<br/>BannerShop</p>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string FormatNok(decimal amount)
        => string.Format(NoCulture, "{0:N2} kr", amount);

    private static string Esc(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
}
