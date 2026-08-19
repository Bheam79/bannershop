using System.ComponentModel.DataAnnotations;
using BannerShop.Api.Models.Catalog;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests;

public class SaveBannerSizeRequestTests
{
    private static SaveBannerSizeRequest Valid() => new()
    {
        Name = "Std",
        MaterialId = 1,
        MinWidthCm = 1,
        MaxWidthCm = 500,
        MinHeightCm = 1,
        MaxHeightCm = 200,
        PricingHeightCm = 154,
        PricingMultiplier = 1
    };

    private static List<ValidationResult> Validate(SaveBannerSizeRequest req)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(req, new ValidationContext(req), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Validate_ValidRequest_ReturnsNoErrors()
    {
        Validate(Valid()).Should().BeEmpty();
    }

    [Fact]
    public void Validate_MaxWidthLessThanMinWidth_ReturnsError()
    {
        var req = Valid();
        req.MinWidthCm = 100;
        req.MaxWidthCm = 99;

        var results = Validate(req);

        results.Should().ContainSingle(r => r.MemberNames.Contains(nameof(SaveBannerSizeRequest.MaxWidthCm)));
    }

    [Fact]
    public void Validate_MaxHeightLessThanMinHeight_ReturnsError()
    {
        var req = Valid();
        req.MinHeightCm = 200;
        req.MaxHeightCm = 199;

        var results = Validate(req);

        results.Should().ContainSingle(r => r.MemberNames.Contains(nameof(SaveBannerSizeRequest.MaxHeightCm)));
    }

    [Fact]
    public void Validate_MaxEqualsMin_IsAllowed()
    {
        var req = Valid();
        req.MinWidthCm = 100;
        req.MaxWidthCm = 100;
        req.MinHeightCm = 50;
        req.MaxHeightCm = 50;

        Validate(req).Should().BeEmpty();
    }

    [Fact]
    public void Validate_BothWidthAndHeightInverted_ReturnsBothErrors()
    {
        var req = Valid();
        req.MinWidthCm = 100;
        req.MaxWidthCm = 1;
        req.MinHeightCm = 100;
        req.MaxHeightCm = 1;

        var results = Validate(req);

        results.Should().HaveCount(2);
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SaveBannerSizeRequest.MaxWidthCm)));
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SaveBannerSizeRequest.MaxHeightCm)));
    }

    [Fact]
    public void Validate_MissingRequiredName_ReturnsError()
    {
        var req = Valid();
        req.Name = "";

        Validate(req).Should().Contain(r => r.MemberNames.Contains(nameof(SaveBannerSizeRequest.Name)));
    }

    [Fact]
    public void Validate_WidthOutOfRange_ReturnsError()
    {
        var req = Valid();
        req.MinWidthCm = 0;

        Validate(req).Should().Contain(r => r.MemberNames.Contains(nameof(SaveBannerSizeRequest.MinWidthCm)));
    }
}
