using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using BannerShop.Api.Controllers.Admin;
using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Api.Services.Email;
using BannerShop.Api.Services.Orders;
using BannerShop.Api.Services.SystemSettings;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
    Console.WriteLine("PASS: " + label);
}

using var db = new BannerShopDbContext(new DbContextOptionsBuilder<BannerShopDbContext>()
    .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
await db.Database.EnsureCreatedAsync();
using var cache = new MemoryCache(new MemoryCacheOptions());
var settings = new SystemSettingsService(db, cache);
var seededKey = (await settings.GetAllAsync()).Single(s => s.Key == AdminOrderNotificationService.SmsKeySetting);
Check(seededKey.IsSensitive && !string.IsNullOrWhiteSpace(seededKey.Value), "SMS key seeded as sensitive setting");
var key = "fixture+key&æ";
await settings.SetValueAsync(AdminOrderNotificationService.SmsKeySetting, key);
var masked = JsonSerializer.Serialize(((OkObjectResult)await new AdminSettingsController(settings, null!).GetAll(default)).Value);
Check(!masked.Contains(key) && masked.Contains(AdminOrderNotificationService.SmsKeySetting), "Admin settings masks SMS key");

db.Users.AddRange(
    new User { Id = 100, Email = "customer@example.test", Phone = "91920208" },
    new User { Id = 101, Email = "admin@example.test", Phone = "91920208", Role = UserRole.Admin },
    new User { Id = 102, Email = "admin2@example.test", Phone = "+47 88 11 22 33", Role = UserRole.Admin },
    new User { Id = 103, Email = "no-phone@example.test", Role = UserRole.Admin },
    new User { Id = 104, Email = "bad-phone@example.test", Phone = "+46 88 11 22 33", Role = UserRole.Admin });
await db.SaveChangesAsync();
var email = new EmailFixture();
using var sms = new SmsFixture();
var log = new LogFixture<AdminOrderNotificationService>();
var notifications = new AdminOrderNotificationService(db, email, sms, settings,
    Options.Create(new AdminOrderNotificationOptions()), log);
var service = new OrderService(db, null!, null!, null!, null!, email, null!,
    new BannerFileStorage(Options.Create(new FileStorageOptions { LocalRoot = "/tmp/bannersh-314-unused/" })),
    Options.Create(new TestingOptions { EnableMockPayment = true, MockPaymentPassword = "fixture" }),
    NullLogger<OrderService>.Instance, notifications);

async Task<Order> NewOrder(OrderType type = OrderType.CustomBanner)
{
    var o = new Order { UserId = 100, Status = OrderStatus.PendingPayment, TotalNok = 1200, OrderType = type };
    db.Orders.Add(o);
    await db.SaveChangesAsync();
    o.StripePaymentIntentId = "pi_fixture_" + o.Id;
    await db.SaveChangesAsync();
    return o;
}

var order = await NewOrder();
Check(email.Messages.Count == 0 && sms.Requests.Count == 0, "Pending/unpaid order sends no alerts");
await service.MarkPaidAsync(order.StripePaymentIntentId!, null);
Check(email.Messages.Any(m => m.To == "admin@example.test"), "Confirmed order sends email to the profile admin (original reproduction)");
Check(email.Messages.Count == 5 && email.Messages.Count(m => m.To == "customer@example.test") == 1,
    "All four admins receive email, customer confirmation preserved");
Check(sms.Requests.Count == 2, "Only two admins with valid profile phones receive SMS");
var numbers = sms.Requests.Select(r => QueryHelpers.ParseQuery(r.Query)["number"].ToString()).Order().ToArray();
Check(numbers.SequenceEqual(new[] { "4788112233", "4791920208" }), "Eight-digit/local and +47 numbers normalized without double country code");
foreach (var request in sms.Requests)
{
    var query = QueryHelpers.ParseQuery(request.Query);
    Check(request.Host == "bheam.omnijoy.com" && request.Port == 6969 && request.AbsolutePath == "/send"
        && query["key"] == key && query["message"].ToString().Contains($"#{order.Id}")
        && query["message"].ToString().Contains("1200,00 kr"), "Gateway URL, encoded secret and Norwegian order message are correct");
}
Check(email.Messages.Where(m => m.To != "customer@example.test").All(m => m.Body.Contains($"/admin/orders/{order.Id}")),
    "Admin emails identify the order and admin detail route");
var mailCount = email.Messages.Count;
var smsCount = sms.Requests.Count;
await service.MarkPaidAsync(order.StripePaymentIntentId!, order.Id);
await service.MarkPaymentFailedAsync(order.StripePaymentIntentId!, null, "fixture");
await service.MarkPaidAsync(order.StripePaymentIntentId!, null);
Check(email.Messages.Count == mailCount && sms.Requests.Count == smsCount, "Repeated payment webhooks do not resend alerts");

// Resolve admins/profile contacts at send time, not from seed or configuration.
var admin = await db.Users.FindAsync(101);
admin!.Phone = "55 66 77 88";
admin.Email = "changed@example.test";
(await db.Users.FindAsync(102))!.Role = UserRole.Customer;
await db.SaveChangesAsync();
var manual = await NewOrder(OrderType.ManualDesign);
await service.MarkPaidAsync(manual.StripePaymentIntentId!, null);
Check(email.Messages.Count == mailCount + 4 && sms.Requests.Count == smsCount + 1
    && email.Messages.Any(m => m.To == "changed@example.test")
    && QueryHelpers.ParseQuery(sms.Requests.Last().Query)["number"] == "4755667788", "Manual order uses current admin roles/email/phone");

mailCount = email.Messages.Count;
smsCount = sms.Requests.Count;
var creditPack = await NewOrder(OrderType.CreditPack);
await service.MarkPaidAsync(creditPack.StripePaymentIntentId!, null);
Check(email.Messages.Count == mailCount && sms.Requests.Count == smsCount, "AI credit pack does not generate production alerts");
var ai = await NewOrder(OrderType.AiBanner);
var mockPay = await service.MockMarkPaidAsync(100, ai.Id, "fixture");
Check(mockPay.Success && sms.Requests.Count == smsCount + 1, "AI banner mock-pay uses the same notification path");

// One failed email must not prevent its SMS or another admin's email.
email.FailRecipient = "changed@example.test";
sms.Fail = true;
var failedDelivery = await NewOrder();
var noPhoneMails = email.Messages.Count(m => m.To == "no-phone@example.test");
await service.MarkPaidAsync(failedDelivery.StripePaymentIntentId!, null);
Check(failedDelivery.Status == OrderStatus.Paid
    && email.Messages.Count(m => m.To == "no-phone@example.test") == noPhoneMails + 1
    && sms.Requests.Count == smsCount + 2, "Email/gateway failure preserves paid order, other admins and independent SMS attempt");
Check(log.Messages.Any(m => m.Contains("Admin SMS failed")) && log.Messages.All(m => !m.Contains(key) && !m.Contains("http://")),
    "SMS failures logged without key or request URI");
email.FailRecipient = null;
sms.Fail = false;
await settings.SetValueAsync(AdminOrderNotificationService.SmsKeySetting, "");
smsCount = sms.Requests.Count;
mailCount = email.Messages.Count;
await service.MarkPaidAsync((await NewOrder()).StripePaymentIntentId!, null);
Check(sms.Requests.Count == smsCount && email.Messages.Count == mailCount + 4, "Blank key disables SMS only, emails still sent");
foreach (var phone in new[] { "91920208", "91 92-02(08)", "+4791920208", "004791920208", "4791920208" })
    Check(AdminOrderNotificationService.NormalizeNorwegianPhone(phone) == "4791920208", "Accepted Norwegian format: " + phone);
foreach (var phone in new string?[] { null, "", "1234567", "123456789", "+4691920208", "abc91920208", "９１９２０２０８" })
    Check(AdminOrderNotificationService.NormalizeNorwegianPhone(phone) is null, "Missing/malformed/foreign phone rejected");
Console.WriteLine("Offline admin notification smoke complete; no email or SMS was sent externally.");

sealed class EmailFixture : IEmailService
{
    public ConcurrentBag<(string To, string Subject, string Body)> Messages { get; } = [];
    public string? FailRecipient { get; set; }
    public Task SendAsync(string to, string subject, string bodyHtml, CancellationToken ct = default)
    {
        if (to == FailRecipient) throw new InvalidOperationException("Fixture email failure");
        Messages.Add((to, subject, bodyHtml));
        return Task.CompletedTask;
    }
}

sealed class SmsFixture : HttpMessageHandler, IHttpClientFactory
{
    public List<Uri> Requests { get; } = [];
    public bool Fail { get; set; }
    public HttpClient CreateClient(string name) => new(this, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(10) };
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        lock (Requests) Requests.Add(request.RequestUri!);
        return Task.FromResult(new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
    }
}

sealed class LogFixture<T> : ILogger<T>
{
    public ConcurrentBag<string> Messages { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => true;
    public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Messages.Add(formatter(state, exception));
}
