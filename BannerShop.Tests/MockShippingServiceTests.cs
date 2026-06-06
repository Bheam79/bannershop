using BannerShop.Api.Services;
using BannerShop.Api.Services.Shipping;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BannerShop.Tests;

public class MockShippingServiceTests
{
    private static MockShippingService CreateService()
    {
        var db = DbHelper.CreateInMemory();
        DbHelper.SeedPricingParameters(db);
        return new MockShippingService(db, NullLogger<MockShippingService>.Instance);
    }

    // ── Standard cost formula ────────────────────────────────────────────────

    [Fact]
    public async Task Calculate_StandardFormula_BasePlusWeightPlusLengthSurcharge()
    {
        // weight=1.0kg, length=150cm
        // standard = 149 + (1.0 × 35) + (150-100) × 0.4 = 149 + 35 + 20 = 204
        var service = CreateService();
        var parcel = new ParcelDimensions(LengthCm: 150m, WidthCm: 15m, HeightCm: 15m, WeightKg: 1.0m);

        var quote = await service.CalculateAsync("0001", "Oslo", parcel);

        quote.Standard.CostNok.Should().Be(204m);
    }

    [Fact]
    public async Task Calculate_LengthUnder100cm_NoLengthSurcharge()
    {
        // weight=2.0kg, length=80cm (< 100cm → no surcharge)
        // standard = 149 + (2.0 × 35) + 0 = 149 + 70 = 219
        var service = CreateService();
        var parcel = new ParcelDimensions(LengthCm: 80m, WidthCm: 15m, HeightCm: 15m, WeightKg: 2.0m);

        var quote = await service.CalculateAsync("0001", null, parcel);

        quote.Standard.CostNok.Should().Be(219m);
    }

    [Fact]
    public async Task Calculate_VeryLightParcel_UsesMinimumWeight()
    {
        // weight=0.1kg → minimum applied = 0.5kg
        // standard = 149 + (0.5 × 35) + 0 = 149 + 17.5 = 166.50
        var service = CreateService();
        var parcel = new ParcelDimensions(LengthCm: 50m, WidthCm: 10m, HeightCm: 10m, WeightKg: 0.1m);

        var quote = await service.CalculateAsync("0001", "Oslo", parcel);

        quote.Standard.CostNok.Should().Be(166.50m);
    }

    // ── Express cost ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Calculate_ExpressCost_IsStandardPlusExpressFee()
    {
        // With express_fee = 500 (seeded)
        var service = CreateService();
        var parcel = new ParcelDimensions(LengthCm: 80m, WidthCm: 15m, HeightCm: 15m, WeightKg: 1.0m);

        var quote = await service.CalculateAsync("0001", "Oslo", parcel);

        quote.Express.CostNok.Should().Be(quote.Standard.CostNok + 500m);
    }

    [Fact]
    public async Task Calculate_ExpressFeeFallsBackToDefault_WhenNotInDb()
    {
        // DB has no pricing params → express_fee defaults to 500m
        var db = DbHelper.CreateInMemory(); // no params seeded
        var service = new MockShippingService(db, NullLogger<MockShippingService>.Instance);
        var parcel = new ParcelDimensions(LengthCm: 80m, WidthCm: 15m, HeightCm: 15m, WeightKg: 1.0m);

        var quote = await service.CalculateAsync("0001", "Oslo", parcel);

        quote.Express.CostNok.Should().Be(quote.Standard.CostNok + 500m);
    }

    // ── Carrier product metadata ─────────────────────────────────────────────

    [Fact]
    public async Task Calculate_ReturnsExpectedCarrierProductIds()
    {
        var service = CreateService();
        var parcel = new ParcelDimensions(80m, 15m, 15m, 1.0m);

        var quote = await service.CalculateAsync("0001", "Oslo", parcel);

        quote.Standard.CarrierProductId.Should().Be("MOCK_STANDARD");
        quote.Express.CarrierProductId.Should().Be("MOCK_STANDARD");
        quote.Standard.EstimatedDays.Should().Be(3);
        quote.Express.EstimatedDays.Should().Be(1);
    }

    // ── Validation ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task Calculate_EmptyPostalCode_ThrowsArgumentException(string? postal)
    {
        var service = CreateService();
        var parcel = new ParcelDimensions(80m, 15m, 15m, 1.0m);

        var act = () => service.CalculateAsync(postal!, "Oslo", parcel);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*postal code*");
    }
}
