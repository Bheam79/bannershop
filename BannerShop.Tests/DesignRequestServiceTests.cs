using BannerShop.Api.Models.DesignRequests;
using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Api.Services.DesignRequests;
using BannerShop.Api.Services.Email;
using BannerShop.Api.Services.Orders.Stripe;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace BannerShop.Tests;

public class DesignRequestServiceTests
{
    private static BannerFileStorage MakeStorage() =>
        new(Options.Create(new FileStorageOptions
        {
            LocalRoot = Path.Combine(Path.GetTempPath(), "bs-tests-" + Guid.NewGuid().ToString("N")),
            PublicBaseUrl = "/files"
        }));

    private static (DesignRequestService service, Mock<IStripePaymentService> stripe, Mock<IDesignRequestJobQueue> queue) MakeService(
        BannerShop.Infrastructure.Data.BannerShopDbContext db)
    {
        var stripe = new Mock<IStripePaymentService>();
        stripe.Setup(s => s.CreatePaymentIntentAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new StripeIntentResult("pi_test_123", "pi_test_123_secret"));

        var queue = new Mock<IDesignRequestJobQueue>();
        queue.Setup(q => q.EnqueueAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .Returns(ValueTask.CompletedTask);

        var images = new Mock<IImageProcessingService>();
        var email = new Mock<IEmailService>();
        var svc = new DesignRequestService(db, stripe.Object, queue.Object, MakeStorage(), images.Object, email.Object, NullLogger<DesignRequestService>.Instance);
        return (svc, stripe, queue);
    }

    private static async Task SeedAsync(BannerShop.Infrastructure.Data.BannerShopDbContext db)
    {
        db.Users.Add(DbHelper.MakeUser(1));
        db.BannerTemplates.Add(new BannerTemplate
        {
            Id = 1, Category = BannerTemplateCategory.Birthday,
            NameNb = "Bursdag", NameEn = "Birthday", SortOrder = 10
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task CreateAiRequestAsync_persists_and_returns_payment_intent()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var (svc, stripe, _) = MakeService(db);

        var result = await svc.CreateAiRequestAsync(1, new CreateAiDesignRequestDto
        {
            TemplateId = 1,
            Language = "nb",
            PersonName = "Ola",
            PersonAge = 40,
            TextContent = "Gratulerer",
            ThemeDescription = "tropisk",
            AspectRatio = "16:9"
        });

        result.Success.Should().BeTrue();
        result.TotalNok.Should().Be(DesignRequestService.AiPriceNok);
        result.ClientSecret.Should().Be("pi_test_123_secret");

        var saved = db.DesignRequests.Single();
        saved.Status.Should().Be(DesignRequestStatus.Pending);
        saved.StripePaymentIntentId.Should().Be("pi_test_123");
        saved.Mode.Should().Be(DesignRequestMode.Ai);
        saved.PriceNok.Should().Be(95m);

        stripe.Verify(s => s.CreatePaymentIntentAsync(It.IsAny<int>(), 1, 95m, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAiRequestAsync_rejects_unknown_template()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var (svc, _, _) = MakeService(db);

        var result = await svc.CreateAiRequestAsync(1, new CreateAiDesignRequestDto
        {
            TemplateId = 9999,
            Language = "nb",
            PersonName = "Ola",
            TextContent = "Hi",
            ThemeDescription = "",
            AspectRatio = "16:9"
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("template");
    }

    [Fact]
    public async Task MarkPaidAndEnqueueAsync_flips_to_InProgress_and_enqueues()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var (svc, _, queue) = MakeService(db);

        await svc.CreateAiRequestAsync(1, new CreateAiDesignRequestDto
        {
            TemplateId = 1, Language = "nb", PersonName = "Ola", TextContent = "Hi",
            ThemeDescription = "x", AspectRatio = "16:9"
        });

        await svc.MarkPaidAndEnqueueAsync("pi_test_123");

        var saved = db.DesignRequests.Single();
        saved.Status.Should().Be(DesignRequestStatus.InProgress);
        queue.Verify(q => q.EnqueueAsync(saved.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkPaidAndEnqueueAsync_is_idempotent_for_already_inprogress_requests()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var (svc, _, queue) = MakeService(db);

        await svc.CreateAiRequestAsync(1, new CreateAiDesignRequestDto
        {
            TemplateId = 1, Language = "nb", PersonName = "Ola", TextContent = "Hi",
            ThemeDescription = "x", AspectRatio = "16:9"
        });

        await svc.MarkPaidAndEnqueueAsync("pi_test_123");
        await svc.MarkPaidAndEnqueueAsync("pi_test_123");

        queue.Verify(q => q.EnqueueAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApproveAsync_requires_AwaitingApproval_state()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        var (svc, _, _) = MakeService(db);

        await svc.CreateAiRequestAsync(1, new CreateAiDesignRequestDto
        {
            TemplateId = 1, Language = "nb", PersonName = "Ola", TextContent = "Hi",
            ThemeDescription = "x", AspectRatio = "16:9"
        });
        var id = db.DesignRequests.Single().Id;

        var cannotApprove = await svc.ApproveAsync(id, 1);
        cannotApprove.Success.Should().BeFalse();

        // Simulate pipeline completion
        var entity = db.DesignRequests.Single();
        entity.Status = DesignRequestStatus.AwaitingApproval;
        await db.SaveChangesAsync();

        var ok = await svc.ApproveAsync(id, 1);
        ok.Success.Should().BeTrue();
        db.DesignRequests.Single().Status.Should().Be(DesignRequestStatus.Final);
    }

    [Fact]
    public async Task ApproveAsync_rejects_other_users()
    {
        using var db = DbHelper.CreateInMemory();
        await SeedAsync(db);
        db.Users.Add(DbHelper.MakeUser(2, "other@example.com"));
        await db.SaveChangesAsync();
        var (svc, _, _) = MakeService(db);

        await svc.CreateAiRequestAsync(1, new CreateAiDesignRequestDto
        {
            TemplateId = 1, Language = "nb", PersonName = "Ola", TextContent = "Hi",
            ThemeDescription = "x", AspectRatio = "16:9"
        });
        var id = db.DesignRequests.Single().Id;
        var entity = db.DesignRequests.Single();
        entity.Status = DesignRequestStatus.AwaitingApproval;
        await db.SaveChangesAsync();

        var result = await svc.ApproveAsync(id, 2);
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Forbidden");
    }
}
