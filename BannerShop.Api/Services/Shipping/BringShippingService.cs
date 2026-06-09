using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using BannerShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BannerShop.Api.Services.Shipping;

/// <summary>
/// Live implementation of <see cref="IShippingService"/> using the Bring Shipping Guide 2.0 API.
/// Results are cached per (postal code, parcel dimensions) for one hour to avoid hammering the API.
/// </summary>
public class BringShippingService : IShippingService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private readonly HttpClient _http;
    private readonly BringOptions _options;
    private readonly IMemoryCache _cache;
    private readonly BannerShopDbContext _db;
    private readonly ILogger<BringShippingService> _logger;

    public BringShippingService(
        HttpClient http,
        IOptions<BringOptions> options,
        IMemoryCache cache,
        BannerShopDbContext db,
        ILogger<BringShippingService> logger)
    {
        _http = http;
        _options = options.Value;
        _cache = cache;
        _db = db;
        _logger = logger;
    }

    public async Task<ShippingQuote> CalculateAsync(
        string toPostalCode,
        string? toCity,
        ParcelDimensions parcel,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(toPostalCode))
            throw new ArgumentException("Destination postal code is required.", nameof(toPostalCode));

        var normalizedPostal = toPostalCode.Trim().Replace(" ", "");
        var cacheKey = BuildCacheKey(normalizedPostal, parcel);

        if (_cache.TryGetValue<ShippingQuote>(cacheKey, out var cached) && cached is not null)
            return cached;

        var expressFeeNok = await ReadExpressFeeAsync(ct);
        var productCodes = (_options.ProductCodes ?? "SERVICEPAKKE")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (productCodes.Count == 0)
            productCodes.Add("SERVICEPAKKE");

        // BANNERSH-143: hardcoded customer number applies the negotiated rate
        // agreement. eVarslingProducts is the catalogue of codes that support
        // Bring e-notification, only sent when EVarsling is enabled.
        var eVarslingProducts = (_options.EVarslingProducts ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        BringConsignmentParties? parties = null;
        if (!string.IsNullOrWhiteSpace(_options.CustomerNumber))
        {
            parties = new BringConsignmentParties
            {
                Sender = new BringConsignmentParty { CustomerNumber = _options.CustomerNumber },
            };
        }

        var request = new BringShipmentRequest
        {
            Consignments =
            {
                new BringConsignment
                {
                    FromCountryCode = _options.SenderCountryCode,
                    FromPostalCode = _options.SenderPostalCode,
                    ToCountryCode = "NO",
                    ToPostalCode = normalizedPostal,
                    Parties = parties,
                    Products = productCodes.Select(p => new BringProductRef
                    {
                        Id = p,
                        AdditionalServices = (_options.EVarsling && eVarslingProducts.Contains(p))
                            ? new List<BringAdditionalService>
                              {
                                  new BringAdditionalService { Id = "EVARSLING" },
                              }
                            : null,
                    }).ToList(),
                    Packages =
                    {
                        new BringPackage
                        {
                            WeightInKg = decimal.Round(parcel.WeightKg, 2),
                            LengthInCm = decimal.Round(parcel.LengthCm, 1),
                            WidthInCm  = decimal.Round(parcel.WidthCm,  1),
                            HeightInCm = decimal.Round(parcel.HeightCm, 1)
                        }
                    }
                }
            }
        };

        // BANNERSH-143: rates endpoint is configurable via BringOptions.RatesPath
        // (defaults to /shippingguide/v2). The /products sub-path is the rates
        // calculator action; the URL is appended onto the configured BaseUrl.
        var ratesUrl = $"{_options.RatesPath.TrimEnd('/')}/products";

        // Serialise the outgoing request for diagnostics (BANNERSH-143 follow-up).
        // Logged at Debug level in normal operation; bumped to Warning when the API
        // returns no usable product so it always appears in production logs.
        var requestJson = JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = false });

        _logger.LogDebug(
            "Bring API outgoing request → POST {Url} | uid={Uid} | customerNumber={CustomerNumber} | body={Body}",
            ratesUrl,
            _options.ApiUid,
            _options.CustomerNumber,
            requestJson);

        BringShipmentResponse? response;
        string rawResponseBody = string.Empty;
        try
        {
            using var http = new HttpRequestMessage(HttpMethod.Post, ratesUrl);
            http.Headers.Add("X-Mybring-API-Uid", _options.ApiUid);
            http.Headers.Add("X-Mybring-API-Key", _options.ApiKey);
            http.Headers.Add("X-Bring-Client-URL", _options.ClientUrl);
            http.Headers.Accept.Clear();
            http.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            http.Content = JsonContent.Create(request);

            using var resp = await _http.SendAsync(http, ct);
            rawResponseBody = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Bring API returned {Status}. Request body: {RequestBody} | Response body: {ResponseBody}",
                    (int)resp.StatusCode,
                    requestJson,
                    rawResponseBody);
                throw new ShippingUnavailableException(
                    $"Bring API returned HTTP {(int)resp.StatusCode}");
            }

            _logger.LogDebug(
                "Bring API response ({Status}): {Body}",
                (int)resp.StatusCode,
                Truncate(rawResponseBody, 2000));

            response = JsonSerializer.Deserialize<BringShipmentResponse>(rawResponseBody);
        }
        catch (ShippingUnavailableException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex,
                "Bring API request failed (network). Request body: {RequestBody}",
                requestJson);
            throw new ShippingUnavailableException("Could not reach Bring API.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "Bring API request timed out. Request body: {RequestBody}",
                requestJson);
            throw new ShippingUnavailableException("Bring API request timed out.", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Bring API returned malformed JSON. Request body: {RequestBody} | Response body: {ResponseBody}",
                requestJson, rawResponseBody);
            throw new ShippingUnavailableException("Bring API returned malformed response.", ex);
        }

        var product = ExtractFirstUsableProduct(response);
        if (product is null)
        {
            // Log everything so the operator can diagnose: request JSON, full response JSON,
            // and a per-product breakdown of why each was skipped (BANNERSH-143 follow-up).
            var productDiagnostics = response?.Consignments?.SelectMany(c => c.Products ?? Enumerable.Empty<BringProductResult>())
                .Select(p =>
                {
                    var errors = p.Errors is { Count: > 0 }
                        ? string.Join("; ", p.Errors.Select(e => $"{e.Code}: {e.Description}"))
                        : "(no errors)";
                    var hasPrice = !string.IsNullOrWhiteSpace(
                        p.Price?.ListPrice?.PriceWithoutAdditionalServices?.AmountWithVAT ??
                        p.Price?.ListPrice?.PriceWithoutAdditionalServices?.AmountWithoutVAT);
                    return $"  [{p.Id}] errors={errors} hasPrice={hasPrice}";
                }) ?? Enumerable.Empty<string>();

            _logger.LogWarning(
                "Bring API returned no usable shipping product.\n" +
                "Request → POST {Url} uid={Uid} customerNumber={CustomerNumber}\n" +
                "Request body: {RequestBody}\n" +
                "Response body: {ResponseBody}\n" +
                "Product diagnostics:\n{ProductDiagnostics}",
                ratesUrl,
                _options.ApiUid,
                _options.CustomerNumber,
                requestJson,
                rawResponseBody,
                string.Join("\n", productDiagnostics));

            throw new ShippingUnavailableException("Bring API returned no usable shipping product.");
        }

        var (cost, days, productId, productName) = product.Value;

        var standard = new ShippingOption(
            CostNok: cost,
            EstimatedDays: days,
            CarrierProductId: productId,
            CarrierProductName: productName);

        // Express uses the same shipping cost; the express PRODUCTION fee is added on top.
        var express = new ShippingOption(
            CostNok: cost + expressFeeNok,
            EstimatedDays: Math.Max(1, days),
            CarrierProductId: productId,
            CarrierProductName: productName);

        var quote = new ShippingQuote(standard, express);
        _cache.Set(cacheKey, quote, CacheTtl);
        return quote;
    }

    private async Task<decimal> ReadExpressFeeAsync(CancellationToken ct)
    {
        var p = await _db.PricingParameters
            .AsNoTracking()
            .Where(x => x.Key == "express_fee")
            .Select(x => (decimal?)x.Value)
            .FirstOrDefaultAsync(ct);
        return p ?? 500m;
    }

    private static (decimal Cost, int Days, string? ProductId, string? ProductName)? ExtractFirstUsableProduct(
        BringShipmentResponse? response)
    {
        var products = response?.Consignments?.SelectMany(c => c.Products ?? Enumerable.Empty<BringProductResult>())
            ?? Enumerable.Empty<BringProductResult>();

        foreach (var p in products)
        {
            // Skip products that returned errors
            if (p.Errors is { Count: > 0 }) continue;

            var amountStr = p.Price?.ListPrice?.PriceWithoutAdditionalServices?.AmountWithVAT
                ?? p.Price?.ListPrice?.PriceWithoutAdditionalServices?.AmountWithoutVAT;
            if (string.IsNullOrWhiteSpace(amountStr)) continue;

            if (!decimal.TryParse(amountStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
                continue;

            var days = p.ExpectedDelivery?.WorkingDays ?? 3;
            return (decimal.Round(amount, 2), days, p.Id, p.GuiInformation?.ProductName ?? p.GuiInformation?.DisplayName);
        }

        return null;
    }

    private static string BuildCacheKey(string postal, ParcelDimensions p)
        => string.Create(CultureInfo.InvariantCulture,
            $"bring:{postal}:{p.LengthCm:0.#}x{p.WidthCm:0.#}x{p.HeightCm:0.#}:{p.WeightKg:0.##}");

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "…";
}
