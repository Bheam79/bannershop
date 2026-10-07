// Targeted service/relational-storage verification; no production DB or external calls.
using BannerShop.Api.Models.Orders;
using BannerShop.Api.Services;
using BannerShop.Api.Services.AiCredits;
using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Api.Services.Email;
using BannerShop.Api.Services.Orders;
using BannerShop.Api.Services.Orders.Stripe;
using BannerShop.Api.Services.Shipping;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
using var db = new BannerShopDbContext(new DbContextOptionsBuilder<BannerShopDbContext>().UseSqlite(connection).Options);
await db.Database.EnsureCreatedAsync();
db.Users.Add(new User { Id = 311, Name = "Phone check", Email = "phone@example.invalid", Phone = "91234567" });
var material = new Material { Id = 311, Name = "Phone check", WeightGsm = 400, WidthCm = 180 };
db.BannerSizes.Add(new BannerSize { Id = 311, Material = material, Name = "Phone check", IsActive = true,
    MinWidthCm = 1, MaxWidthCm = 400, MinHeightCm = 1, MaxHeightCm = 180, PricingHeightCm = 180, PricingMultiplier = 1, FixedPrice = 699 });
await db.SaveChangesAsync();
var stripe = new Mock<IStripePaymentService>();
stripe.Setup(s => s.CreatePaymentIntentAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(new StripeIntentResult("pi_offline_phone", "offline_secret"));
var shipping = new Mock<IShippingService>();
shipping.Setup(s => s.CalculateAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<ParcelDimensions>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(new ShippingQuote(new ShippingOption(200, 3, "offline", "Offline"), new ShippingOption(300, 1, "offline", "Offline")));
var email = new Mock<IEmailService>();
var storage = new BannerFileStorage(Options.Create(new FileStorageOptions { LocalRoot = "/tmp/bannersh-311-storage" }));
var service = new OrderService(db, new PricingService(db), shipping.Object, new ParcelCalculator(db), stripe.Object,
    email.Object, Mock.Of<IAiCreditService>(), storage, Options.Create(new TestingOptions()), NullLogger<OrderService>.Instance);
var admin = new AdminOrderService(db, email.Object, storage, stripe.Object, service, NullLogger<AdminOrderService>.Instance);
void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
CreateOrderDraftRequest Request(string? phone, DeliveryType delivery = DeliveryType.Pickup) => new() {
    CustomerPhone = phone, DeliveryType = delivery,
    ShippingAddress = delivery == DeliveryType.Pickup ? null : new() { Line1 = "Offline 1", PostalCode = "4626", City = "Kristiansand" },
    Items = [new() { BannerSizeId = 311, WidthCm = 300, HeightCm = 180, Quantity = 1 }]
};
foreach (var invalid in new[] { "", " ", "abc", "12345", "+1234567890123456", "91234<script>", new string('1', 51) }) {
    var before = await db.Orders.CountAsync();
    Check(!(await service.CreateDraftAsync(311, Request(invalid))).Success && await db.Orders.CountAsync() == before,
        "invalid phone rejected before order creation: " + invalid);
}
foreach (var delivery in Enum.GetValues<DeliveryType>()) {
    var result = await service.CreateDraftAsync(311, Request("  +47 912 34 567  ", delivery));
    Check(result.Success, delivery + " draft created");
    db.ChangeTracker.Clear();
    Check((await service.GetMineAsync(311, result.OrderId))?.CustomerPhone == "+47 912 34 567", delivery + " customer detail returns saved phone");
    Check((await admin.GetAnyAsync(result.OrderId))?.CustomerPhone == "+47 912 34 567", delivery + " admin detail returns saved phone");
    Check((await db.Orders.FindAsync(result.OrderId))?.CustomerPhone == "+47 912 34 567", delivery + " relational row preserves trimmed contact");
}
var customer = await db.Users.FindAsync(311);
customer!.Phone = "99999999";
await db.SaveChangesAsync();
db.ChangeTracker.Clear();
var first = await db.Orders.OrderBy(o => o.Id).FirstAsync();
Check((await service.GetMineAsync(311, first.Id))?.CustomerPhone == "+47 912 34 567", "profile change does not overwrite checkout phone");
var legacy = await service.CreateDraftAsync(311, Request(null));
Check(legacy.Success, "older clients can omit phone");
Check((await service.GetMineAsync(311, legacy.OrderId))?.CustomerPhone == "99999999", "legacy order uses available profile phone");
customer = await db.Users.FindAsync(311);
customer!.Phone = null;
await db.SaveChangesAsync();
Check((await service.GetMineAsync(311, legacy.OrderId))?.CustomerPhone == null, "legacy order without phone remains readable");
