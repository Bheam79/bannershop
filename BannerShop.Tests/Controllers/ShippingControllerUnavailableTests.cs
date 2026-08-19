using System.Net;
using System.Net.Http.Json;
using BannerShop.Api.Services;
using BannerShop.Api.Services.Shipping;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BannerShop.Tests.Controllers;

/// <summary>
/// Covers the ShippingController.Calculate branch that MockShippingService can't
/// exercise: the ShippingUnavailableException → 503 mapping. A stub IShippingService
/// that always throws is swapped in via a dedicated factory (same pattern as the
/// rate-limit test factories).
/// </summary>
public class ShippingControllerUnavailableTests : IClassFixture<ShippingUnavailableTestFactory>
{
    private readonly ShippingUnavailableTestFactory _factory;

    public ShippingControllerUnavailableTests(ShippingUnavailableTestFactory factory)
    {
        _factory = factory;
        _factory.SeedDatabase(db =>
        {
            if (!db.Materials.Any())
            {
                DbHelper.SeedPricingParameters(db);
                DbHelper.SeedCatalog(db);
            }
        });
    }

    [Fact]
    public async Task Calculate_CarrierUnavailable_Returns503WithDetail()
    {
        var client = _factory.CreateClient();
        var req = new
        {
            materialId = 1,
            widthCm = 300,
            heightCm = 150,
            qty = 1,
            postalCode = "0001",
            city = "Oslo",
            packingMode = "Folded"
        };

        var response = await client.PostAsJsonAsync("/api/shipping/calculate", req);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("temporarily unavailable");
        body.Should().Contain(ThrowingShippingService.Reason);
    }
}

/// <summary>Always throws ShippingUnavailableException, simulating a carrier outage.</summary>
public class ThrowingShippingService : IShippingService
{
    public const string Reason = "carrier down for test";

    public Task<ShippingQuote> CalculateAsync(
        string toPostalCode, string? toCity, ParcelDimensions parcel, CancellationToken ct = default)
        => throw new ShippingUnavailableException(Reason);
}

public class ShippingUnavailableTestFactory : TestWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            var descriptors = services.Where(d => d.ServiceType == typeof(IShippingService)).ToList();
            foreach (var d in descriptors)
                services.Remove(d);
            services.AddScoped<IShippingService, ThrowingShippingService>();
        });
    }
}
