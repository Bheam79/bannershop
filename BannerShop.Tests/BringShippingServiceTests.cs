using System.Net;
using System.Text;
using System.Text.Json;
using BannerShop.Api.Services;
using BannerShop.Api.Services.Shipping;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BannerShop.Tests;

/// <summary>
/// Exercises the real <see cref="BringShippingService"/> (BANNERSH-143) — the live
/// Bring Shipping Guide 2.0 HTTP wrapper — via a stub <see cref="HttpMessageHandler"/>.
/// It carries <c>[ExcludeFromCodeCoverage(Justification = "tested via integration")]</c>
/// like the other HTTP-wrapper classes (<c>OpenAiImageService</c>, <c>RealEsrganUpscalingService</c>)
/// but had ZERO direct test refs — only <see cref="MockShippingService"/> (the test double
/// swapped in by <c>TestWebApplicationFactory</c>) is covered. This locks down: request
/// construction (customer number/eVarsling/product-code parsing), response parsing
/// (AmountWithVAT→AmountWithoutVAT fallback, error-product skipping), the express-fee-on-top
/// math, the one-hour per-(postal,parcel) cache, and every carrier-failure →
/// <see cref="ShippingUnavailableException"/> path (non-2xx, malformed JSON, network failure,
/// no usable product).
/// </summary>
public class BringShippingServiceTests
{
    private static readonly ParcelDimensions Parcel = new(
        LengthCm: 150m, WidthCm: 15m, HeightCm: 15m, WeightKg: 1.2m);

    private const string SuccessBody = """
        {
          "consignments": [
            {
              "products": [
                {
                  "id": "SERVICEPAKKE",
                  "guiInformation": { "productName": "Servicepakke" },
                  "price": { "listPrice": { "priceWithoutAdditionalServices": { "amountWithVAT": "245.50" } } },
                  "expectedDelivery": { "workingDays": 3 }
                }
              ]
            }
          ]
        }
        """;

    // ── Success / response parsing ──────────────────────────────────────────

    [Fact]
    public async Task Calculate_Success_ReturnsParsedCostDaysAndProduct()
    {
        var stub = new StubHandler(HttpStatusCode.OK, SuccessBody);
        var (service, _) = CreateService(stub);

        var quote = await service.CalculateAsync("0150", "Oslo", Parcel);

        quote.Standard.CostNok.Should().Be(245.50m);
        quote.Standard.EstimatedDays.Should().Be(3);
        quote.Standard.CarrierProductId.Should().Be("SERVICEPAKKE");
        quote.Standard.CarrierProductName.Should().Be("Servicepakke");
    }

    [Fact]
    public async Task Calculate_ExpressCost_IsStandardCostPlusExpressFeeFromDb()
    {
        var stub = new StubHandler(HttpStatusCode.OK, SuccessBody);
        var (service, db) = CreateService(stub);
        DbHelper.SeedPricingParameters(db);
        db.PricingParameters.First(p => p.Key == "express_fee").Value = 350m;
        db.SaveChanges();

        var quote = await service.CalculateAsync("0150", "Oslo", Parcel);

        quote.Express.CostNok.Should().Be(quote.Standard.CostNok + 350m);
        quote.Express.CarrierProductId.Should().Be(quote.Standard.CarrierProductId);
    }

    [Fact]
    public async Task Calculate_ExpressFee_FallsBackTo500_WhenNotInDb()
    {
        var stub = new StubHandler(HttpStatusCode.OK, SuccessBody);
        var (service, _) = CreateService(stub); // no pricing params seeded

        var quote = await service.CalculateAsync("0150", "Oslo", Parcel);

        quote.Express.CostNok.Should().Be(quote.Standard.CostNok + 500m);
    }

    [Fact]
    public async Task Calculate_AmountWithVATMissing_FallsBackToAmountWithoutVAT()
    {
        var body = """
            {
              "consignments": [ { "products": [ {
                "id": "SERVICEPAKKE",
                "price": { "listPrice": { "priceWithoutAdditionalServices": { "amountWithoutVAT": "196.40" } } },
                "expectedDelivery": { "workingDays": 2 }
              } ] } ]
            }
            """;
        var stub = new StubHandler(HttpStatusCode.OK, body);
        var (service, _) = CreateService(stub);

        var quote = await service.CalculateAsync("0150", "Oslo", Parcel);

        quote.Standard.CostNok.Should().Be(196.40m);
    }

    [Fact]
    public async Task Calculate_MissingWorkingDays_DefaultsToThreeDays()
    {
        var body = """
            {
              "consignments": [ { "products": [ {
                "id": "SERVICEPAKKE",
                "price": { "listPrice": { "priceWithoutAdditionalServices": { "amountWithVAT": "100" } } }
              } ] } ]
            }
            """;
        var stub = new StubHandler(HttpStatusCode.OK, body);
        var (service, _) = CreateService(stub);

        var quote = await service.CalculateAsync("0150", "Oslo", Parcel);

        quote.Standard.EstimatedDays.Should().Be(3);
    }

    [Fact]
    public async Task Calculate_FirstProductHasErrors_SkipsToNextUsableProduct()
    {
        var body = """
            {
              "consignments": [ { "products": [
                {
                  "id": "BPAKKE_DOR-DOR",
                  "errors": [ { "code": "TOO_LARGE", "description": "Package exceeds limits" } ]
                },
                {
                  "id": "PA_DOREN",
                  "price": { "listPrice": { "priceWithoutAdditionalServices": { "amountWithVAT": "310.00" } } },
                  "expectedDelivery": { "workingDays": 4 }
                }
              ] } ]
            }
            """;
        var stub = new StubHandler(HttpStatusCode.OK, body);
        var (service, _) = CreateService(stub);

        var quote = await service.CalculateAsync("0150", "Oslo", Parcel);

        quote.Standard.CarrierProductId.Should().Be("PA_DOREN");
        quote.Standard.CostNok.Should().Be(310.00m);
    }

    [Fact]
    public async Task Calculate_AllProductsHaveErrors_ThrowsShippingUnavailable()
    {
        var body = """
            {
              "consignments": [ { "products": [
                { "id": "SERVICEPAKKE", "errors": [ { "code": "X", "description": "no can do" } ] }
              ] } ]
            }
            """;
        var stub = new StubHandler(HttpStatusCode.OK, body);
        var (service, _) = CreateService(stub);

        var act = () => service.CalculateAsync("0150", "Oslo", Parcel);

        await act.Should().ThrowAsync<ShippingUnavailableException>();
    }

    [Fact]
    public async Task Calculate_NoProductsAtAll_ThrowsShippingUnavailable()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """{ "consignments": [] }""");
        var (service, _) = CreateService(stub);

        var act = () => service.CalculateAsync("0150", "Oslo", Parcel);

        await act.Should().ThrowAsync<ShippingUnavailableException>()
            .WithMessage("*no usable shipping product*");
    }

    // ── Carrier / transport failures ─────────────────────────────────────────

    [Fact]
    public async Task Calculate_NonSuccessStatusCode_ThrowsShippingUnavailable()
    {
        var stub = new StubHandler(HttpStatusCode.InternalServerError, "upstream boom");
        var (service, _) = CreateService(stub);

        var act = () => service.CalculateAsync("0150", "Oslo", Parcel);

        await act.Should().ThrowAsync<ShippingUnavailableException>()
            .WithMessage("*HTTP 500*");
    }

    [Fact]
    public async Task Calculate_MalformedJson_ThrowsShippingUnavailable()
    {
        var stub = new StubHandler(HttpStatusCode.OK, "not json at all");
        var (service, _) = CreateService(stub);

        var act = () => service.CalculateAsync("0150", "Oslo", Parcel);

        await act.Should().ThrowAsync<ShippingUnavailableException>()
            .WithMessage("*malformed*");
    }

    [Fact]
    public async Task Calculate_NetworkFailure_ThrowsShippingUnavailable()
    {
        var stub = new ThrowingHandler(new HttpRequestException("connection refused"));
        var (service, _) = CreateService(stub);

        var act = () => service.CalculateAsync("0150", "Oslo", Parcel);

        await act.Should().ThrowAsync<ShippingUnavailableException>()
            .WithMessage("*Could not reach Bring API*");
    }

    // ── Validation ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public async Task Calculate_EmptyPostalCode_ThrowsArgumentException_WithoutCallingBring(string? postal)
    {
        var stub = new StubHandler(HttpStatusCode.OK, SuccessBody);
        var (service, _) = CreateService(stub);

        var act = () => service.CalculateAsync(postal!, "Oslo", Parcel);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*postal code*");
        stub.LastRequestBody.Should().BeNull("Bring must never be called for an invalid postal code");
    }

    // ── Caching ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Calculate_SamePostalAndParcel_SecondCallHitsCacheNotHttp()
    {
        var stub = new StubHandler(HttpStatusCode.OK, SuccessBody);
        var (service, _) = CreateService(stub);

        await service.CalculateAsync("0150", "Oslo", Parcel);
        stub.CallCount.Should().Be(1);
        await service.CalculateAsync("0150", "Oslo", Parcel);

        stub.CallCount.Should().Be(1, "identical (postal, parcel) must be served from the 1h cache");
    }

    [Fact]
    public async Task Calculate_DifferentPostalCode_BypassesCache()
    {
        var stub = new StubHandler(HttpStatusCode.OK, SuccessBody);
        var (service, _) = CreateService(stub);

        await service.CalculateAsync("0150", "Oslo", Parcel);
        await service.CalculateAsync("0250", "Oslo", Parcel);

        stub.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task Calculate_PostalCodeWithSpaces_NormalizedSameAsWithout()
    {
        var stub = new StubHandler(HttpStatusCode.OK, SuccessBody);
        var (service, _) = CreateService(stub);

        await service.CalculateAsync("0150", "Oslo", Parcel);
        await service.CalculateAsync("0 1 5 0", "Oslo", Parcel);

        stub.CallCount.Should().Be(1, "whitespace-normalized postal code must hit the same cache key");
    }

    // ── Request construction ─────────────────────────────────────────────────

    [Fact]
    public async Task Calculate_SendsCustomerNumber_WhenConfigured()
    {
        var stub = new StubHandler(HttpStatusCode.OK, SuccessBody);
        var (service, _) = CreateService(stub, opts => opts.CustomerNumber = "20027039252");

        await service.CalculateAsync("0150", "Oslo", Parcel);

        using var doc = JsonDocument.Parse(stub.LastRequestBody!);
        doc.RootElement.GetProperty("consignments")[0]
            .GetProperty("parties").GetProperty("sender").GetProperty("customerNumber")
            .GetString().Should().Be("20027039252");
    }

    [Fact]
    public async Task Calculate_OmitsParties_WhenCustomerNumberBlank()
    {
        var stub = new StubHandler(HttpStatusCode.OK, SuccessBody);
        var (service, _) = CreateService(stub, opts => opts.CustomerNumber = "");

        await service.CalculateAsync("0150", "Oslo", Parcel);

        using var doc = JsonDocument.Parse(stub.LastRequestBody!);
        doc.RootElement.GetProperty("consignments")[0].TryGetProperty("parties", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Calculate_ProductCodes_ParsedFromCommaSeparatedList()
    {
        var stub = new StubHandler(HttpStatusCode.OK, SuccessBody);
        var (service, _) = CreateService(stub, opts => opts.ProductCodes = "SERVICEPAKKE, PA_DOREN ,EKSPRESS09");

        await service.CalculateAsync("0150", "Oslo", Parcel);

        using var doc = JsonDocument.Parse(stub.LastRequestBody!);
        var products = doc.RootElement.GetProperty("consignments")[0].GetProperty("products");
        products.GetArrayLength().Should().Be(3);
        products.EnumerateArray().Select(p => p.GetProperty("id").GetString())
            .Should().Equal("SERVICEPAKKE", "PA_DOREN", "EKSPRESS09");
    }

    [Fact]
    public async Task Calculate_BlankProductCodes_FallsBackToServicepakke()
    {
        var stub = new StubHandler(HttpStatusCode.OK, SuccessBody);
        var (service, _) = CreateService(stub, opts => opts.ProductCodes = "");

        await service.CalculateAsync("0150", "Oslo", Parcel);

        using var doc = JsonDocument.Parse(stub.LastRequestBody!);
        var products = doc.RootElement.GetProperty("consignments")[0].GetProperty("products");
        products.GetArrayLength().Should().Be(1);
        products[0].GetProperty("id").GetString().Should().Be("SERVICEPAKKE");
    }

    [Fact]
    public async Task Calculate_EVarslingEnabled_AddsAdditionalServiceOnlyForListedProducts()
    {
        var stub = new StubHandler(HttpStatusCode.OK, SuccessBody);
        var (service, _) = CreateService(stub, opts =>
        {
            opts.ProductCodes = "SERVICEPAKKE,PA_DOREN";
            opts.EVarsling = true;
            opts.EVarslingProducts = "SERVICEPAKKE";
        });

        await service.CalculateAsync("0150", "Oslo", Parcel);

        using var doc = JsonDocument.Parse(stub.LastRequestBody!);
        var products = doc.RootElement.GetProperty("consignments")[0].GetProperty("products")
            .EnumerateArray().ToList();
        products[0].GetProperty("additionalServices")[0].GetProperty("id").GetString().Should().Be("EVARSLING");
        products[1].TryGetProperty("additionalServices", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Calculate_EVarslingDisabled_NeverAddsAdditionalServices()
    {
        var stub = new StubHandler(HttpStatusCode.OK, SuccessBody);
        var (service, _) = CreateService(stub, opts =>
        {
            opts.EVarsling = false;
            opts.EVarslingProducts = "SERVICEPAKKE";
        });

        await service.CalculateAsync("0150", "Oslo", Parcel);

        using var doc = JsonDocument.Parse(stub.LastRequestBody!);
        doc.RootElement.GetProperty("consignments")[0].GetProperty("products")[0]
            .TryGetProperty("additionalServices", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Calculate_PackageDimensions_MappedFromParcelInGramsAndCm()
    {
        var stub = new StubHandler(HttpStatusCode.OK, SuccessBody);
        var (service, _) = CreateService(stub);
        var parcel = new ParcelDimensions(
            LengthCm: 123.45m, WidthCm: 20m, HeightCm: 20m, WeightKg: 2.5m,
            NonStackable: true, VolumeSpecial: true);

        await service.CalculateAsync("0150", "Oslo", parcel);

        using var doc = JsonDocument.Parse(stub.LastRequestBody!);
        var pkg = doc.RootElement.GetProperty("consignments")[0].GetProperty("packages")[0];
        pkg.GetProperty("grossWeight").GetDecimal().Should().Be(2500m);
        pkg.GetProperty("length").GetDecimal().Should().Be(123.4m, "decimal.Round uses banker's rounding (round-half-to-even)");
        pkg.GetProperty("nonStackable").GetBoolean().Should().BeTrue();
        pkg.GetProperty("volumeSpecial").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Calculate_RatesUrl_AppendsProductsToConfiguredRatesPath()
    {
        var stub = new StubHandler(HttpStatusCode.OK, SuccessBody);
        var (service, _) = CreateService(stub, opts => opts.RatesPath = "/shippingguide/v2/");

        await service.CalculateAsync("0150", "Oslo", Parcel);

        stub.LastRequestUri!.AbsolutePath.Should().Be("/shippingguide/v2/products");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (BringShippingService Service, BannerShop.Infrastructure.Data.BannerShopDbContext Db) CreateService(
        HttpMessageHandler handler, Action<BringOptions>? configure = null)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.bring.test") };
        var bringOptions = new BringOptions();
        configure?.Invoke(bringOptions);
        var db = DbHelper.CreateInMemory();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new BringShippingService(
            http, Options.Create(bringOptions), cache, db, NullLogger<BringShippingService>.Instance);
        return (service, db);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public Uri? LastRequestUri { get; private set; }
        public string? LastRequestBody { get; private set; }
        public int CallCount { get; private set; }

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestUri = request.RequestUri;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _exception;
        public string? LastRequestBody => null;

        public ThrowingHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw _exception;
    }
}
