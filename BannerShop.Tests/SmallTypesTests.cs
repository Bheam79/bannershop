using BannerShop.Api.Models.DesignRequests;
using BannerShop.Api.Services.DesignRequests.Replicate;
using BannerShop.Core;
using BannerShop.Core.Entities;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests;

/// <summary>
/// Quick-coverage tests for small DTO / entity / options types that would
/// otherwise stay at 0% because no other test exercises them.
/// </summary>
public class SmallTypesTests
{
    // ── DesignRequestListItemDto ─────────────────────────────────────────────

    [Fact]
    public void DesignRequestListItemDto_DefaultConstruction_HasExpectedDefaults()
    {
        var dto = new DesignRequestListItemDto();

        dto.Id.Should().Be(0);
        dto.Mode.Should().Be(string.Empty);
        dto.Status.Should().Be(string.Empty);
        dto.AspectRatio.Should().Be(string.Empty);
        dto.PersonName.Should().Be(string.Empty);
        dto.ThemeDescription.Should().Be(string.Empty);
        dto.PreviewUrl.Should().BeNull();
    }

    [Fact]
    public void DesignRequestListItemDto_SetProperties_ArePreserved()
    {
        var dto = new DesignRequestListItemDto
        {
            Id = 42,
            BannerTemplateId = 3,
            Mode = "Ai",
            Status = "Pending",
            AspectRatio = "16:9",
            PriceNok = 495m,
            CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            PreviewUrl = "https://example.com/preview.jpg",
            PersonName = "Ola Nordmann",
            ThemeDescription = "Tropical vibes"
        };

        dto.Id.Should().Be(42);
        dto.Mode.Should().Be("Ai");
        dto.Status.Should().Be("Pending");
        dto.PersonName.Should().Be("Ola Nordmann");
        dto.PreviewUrl.Should().Be("https://example.com/preview.jpg");
    }

    // ── DesignRequestRevisionDto ─────────────────────────────────────────────

    [Fact]
    public void DesignRequestRevisionDto_DefaultConstruction_HasExpectedDefaults()
    {
        var dto = new DesignRequestRevisionDto();

        dto.Id.Should().Be(0);
        dto.RevisionNumber.Should().Be(0);
        dto.CustomerComment.Should().Be(string.Empty);
    }

    [Fact]
    public void DesignRequestRevisionDto_SetProperties_ArePreserved()
    {
        var dto = new DesignRequestRevisionDto
        {
            Id = 7,
            RevisionNumber = 2,
            CustomerComment = "Please change the font",
            CreatedAt = new DateTime(2025, 3, 15, 0, 0, 0, DateTimeKind.Utc)
        };

        dto.Id.Should().Be(7);
        dto.RevisionNumber.Should().Be(2);
        dto.CustomerComment.Should().Be("Please change the font");
    }

    // ── RegenerateAiResponseDto ───────────────────────────────────────────────

    [Fact]
    public void RegenerateAiResponseDto_DefaultConstruction_HasExpectedDefaults()
    {
        var dto = new RegenerateAiResponseDto();

        dto.GenerationId.Should().Be(0);
        dto.CreditsRemaining.Should().Be(0);
        dto.NewDesignRequestId.Should().BeNull();
    }

    [Fact]
    public void RegenerateAiResponseDto_WithNewDesignRequestId_IsPreserved()
    {
        var dto = new RegenerateAiResponseDto
        {
            GenerationId = 99,
            CreditsRemaining = 3,
            NewDesignRequestId = 42
        };

        dto.GenerationId.Should().Be(99);
        dto.CreditsRemaining.Should().Be(3);
        dto.NewDesignRequestId.Should().Be(42);
    }

    // ── StoredFile (record) ───────────────────────────────────────────────────

    [Fact]
    public void StoredFile_Construction_PreservesValues()
    {
        var sf = new StoredFile("uploads/foo.jpg", "https://cdn.example.com/foo.jpg", 12345L);

        sf.StoragePath.Should().Be("uploads/foo.jpg");
        sf.PublicUrl.Should().Be("https://cdn.example.com/foo.jpg");
        sf.SizeBytes.Should().Be(12345L);
    }

    [Fact]
    public void StoredFile_EqualityByValue_Works()
    {
        var a = new StoredFile("path", "url", 100);
        var b = new StoredFile("path", "url", 100);
        a.Should().Be(b);
    }

    // ── DesignRequestRevision (entity) ────────────────────────────────────────

    [Fact]
    public void DesignRequestRevision_DefaultConstruction_HasExpectedDefaults()
    {
        var rev = new DesignRequestRevision();

        rev.Id.Should().Be(0);
        rev.DesignRequestId.Should().Be(0);
        rev.RevisionNumber.Should().Be(0);
        rev.CustomerComment.Should().Be(string.Empty);
    }

    [Fact]
    public void DesignRequestRevision_SetProperties_ArePreserved()
    {
        var rev = new DesignRequestRevision
        {
            Id = 5,
            DesignRequestId = 10,
            RevisionNumber = 1,
            CustomerComment = "Make it blue"
        };

        rev.Id.Should().Be(5);
        rev.DesignRequestId.Should().Be(10);
        rev.RevisionNumber.Should().Be(1);
        rev.CustomerComment.Should().Be("Make it blue");
    }

    // ── ReplicateOptions ──────────────────────────────────────────────────────

    [Fact]
    public void ReplicateOptions_Defaults_AreSet()
    {
        var opts = new ReplicateOptions();

        opts.ApiToken.Should().Be(string.Empty);
        opts.RealEsrganModelVersion.Should().NotBeNullOrWhiteSpace();
        opts.BaseUrl.Should().Contain("replicate");
        opts.TimeoutSeconds.Should().BeGreaterThan(0);
        opts.PollIntervalMs.Should().BeGreaterThan(0);
        opts.MaxPollSeconds.Should().BeGreaterThan(0);
        ReplicateOptions.SectionName.Should().Be("Replicate");
    }

    [Fact]
    public void ReplicateOptions_SetProperties_ArePreserved()
    {
        var opts = new ReplicateOptions
        {
            ApiToken = "r8_test",
            TimeoutSeconds = 120,
            PollIntervalMs = 5000,
            MaxPollSeconds = 300
        };

        opts.ApiToken.Should().Be("r8_test");
        opts.TimeoutSeconds.Should().Be(120);
    }
}
