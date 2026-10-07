// Standalone offline verification: no project test suite, real credentials or provider calls.
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using BannerShop.Api.Controllers.Admin;
using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Api.Services.Orders;
using BannerShop.Api.Services.SystemSettings;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

var root = Directory.CreateTempSubdirectory("bannershop-312-").FullName;
void Check(bool success, string label)
{
    if (!success) throw new Exception("FAIL: " + label);
    Console.WriteLine("PASS: " + label);
}
async Task Throws(Func<Task> action, int status, string label)
{
    try { await action(); throw new Exception("FAIL: " + label); }
    catch (UpscaleException e) { Check(e.StatusCode == status, label); }
}
try
{
    using (var image = new Image<Rgba32>(3, 2, new Rgba32(10, 20, 30)))
        await image.SaveAsPngAsync(root + "/original.png");
    var original = await File.ReadAllBytesAsync(root + "/original.png");
    var storage = new BannerFileStorage(Options.Create(new FileStorageOptions { LocalRoot = root + "/" }));
    var images = new ImageProcessingService();
    var fixture = new FalFixture(original);
    FalOrderUpscaleService Service() => new(storage, images, fixture);
    var service = Service();
    Check(await service.GetCachedPathAsync("original.png", 2, default) is null, "No cache before first upscale");
    await Throws(() => service.PrepareAsync("original.png", 2, null, default), 503, "Missing API key gives actionable 503");
    await Throws(() => service.PrepareAsync("original.png", 3, "fixture-key", default), 400, "Only 2x and 4x accepted");
    await Throws(() => service.PrepareAsync("missing.png", 2, "fixture-key", default), 404, "Missing original rejected before provider call");
    await Throws(() => service.PrepareAsync("../outside.png", 2, "fixture-key", default), 400, "Source path traversal rejected");
    Check(fixture.Submissions == 0, "Validation failures do not spend fal credits");

    var concurrent = await Task.WhenAll(Enumerable.Range(0, 6)
        .Select(_ => service.PrepareAsync("original.png", 2, "fixture-key", default)));
    Check(concurrent.All(ready => !ready) && fixture.Submissions == 1, "Six simultaneous clicks submit only one paid job");
    // A new service instance mimics a backend restart (no in-memory handle/cache).
    service = Service();
    Check(!await service.PrepareAsync("original.png", 2, "fixture-key", default) && fixture.Submissions == 1,
        "Restart resumes durable queue handle without generating twice");
    fixture.Complete = true;
    fixture.BrokenDownload = true;
    try { await service.PrepareAsync("original.png", 2, "fixture-key", default); throw new Exception("Invalid PNG accepted"); }
    catch (UnknownImageFormatException) { }
    Check(await service.GetCachedPathAsync("original.png", 2, default) is null, "Invalid download never becomes cache");
    fixture.BrokenDownload = false;
    Check(await service.PrepareAsync("original.png", 2, "fixture-key", default), "Completed queue result downloads locally");
    var cached2 = await service.GetCachedPathAsync("original.png", 2, default);
    Check(cached2 is not null && (await File.ReadAllBytesAsync(cached2)).SequenceEqual(original), "Cached file contains verified downloaded PNG bytes");
    var calls = fixture.Calls;
    service = Service();
    Check(await service.PrepareAsync("original.png", 2, null, default) && fixture.Calls == calls,
        "Repeated download after restart/key removal makes no provider calls");
    Check(!await service.PrepareAsync("original.png", 4, "fixture-key", default) && fixture.Submissions == 2,
        "4x has independent cache and sends requested factor");
    Check(await service.PrepareAsync("original.png", 4, "fixture-key", default), "4x download completes");
    Check(await service.GetCachedPathAsync("original.png", 4, default) != cached2, "2x and 4x never collide");
    Check((await File.ReadAllBytesAsync(root + "/original.png")).SequenceEqual(original), "Original file unchanged by both upscales");

    // Exercise real controller path selection, item scoping, settings storage/masking.
    using var db = new BannerShopDbContext(new DbContextOptionsBuilder<BannerShopDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    await db.Database.EnsureCreatedAsync();
    using var memory = new MemoryCache(new MemoryCacheOptions());
    var settings = new SystemSettingsService(db, memory);
    var settingsController = new AdminSettingsController(settings, null!);
    Check((await settings.GetAllAsync()).Any(s => s.Key == "fal_api_key" && s.IsSensitive && s.Value == ""),
        "fal API key is seeded blank and sensitive");
    await settingsController.Update("fal_api_key", new UpdateSettingRequest("fixture-key"), default);
    var masked = JsonSerializer.Serialize(((OkObjectResult)await settingsController.GetAll(default)).Value);
    Check(masked.Contains("fal_api_key") && !masked.Contains("fixture-key"), "Admin settings save and mask fal key");

    var order = new Order { Id = 900 };
    var custom = new OrderItem { Id = 901, Order = order, BannerDesign = new BannerDesign { StoragePath = "original.png" } };
    var aiRequest = new DesignRequest { Id = 903, Mode = DesignRequestMode.Ai,
        FinalCroppedStoragePath = "original.png", AiResultStoragePath = "non-selected-original.png" };
    var ai = new OrderItem { Id = 902, Order = order, DesignRequest = aiRequest };
    var manualRequest = new DesignRequest { Id = 905, Mode = DesignRequestMode.Manual, FinalCroppedStoragePath = "original.png" };
    var manual = new OrderItem { Id = 904, Order = order, DesignRequest = manualRequest };
    db.OrderItems.AddRange(custom, ai, manual);
    await db.SaveChangesAsync();
    var controller = new AdminOrderUpscalesController(db, service, settings, NullLogger<AdminOrderUpscalesController>.Instance);
    foreach (var item in new[] { custom, ai, manual })
    {
        Check(await controller.Prepare(900, item.Id, 2, default) is OkObjectResult, $"Item {item.Id}: upload/AI/manual canonical print file used");
        var download = await controller.Download(900, item.Id, 2, default) as PhysicalFileResult;
        Check(download?.ContentType == "image/png" && download.FileDownloadName == $"ordre-900-banner-{item.Id}-2x.png",
            $"Item {item.Id}: authenticated attachment filename and PNG type");
    }
    Check(await controller.Prepare(999, 901, 2, default) is NotFoundObjectResult, "Item cannot be accessed through another order");
    Check(await controller.Prepare(900, 901, 8, default) is ObjectResult { StatusCode: 400 }, "Controller rejects unsupported scale");
    order.Deleted = true;
    await db.SaveChangesAsync();
    Check(await controller.Download(900, 901, 2, default) is NotFoundResult, "Deleted orders cannot download cached derivatives");
    var authorize = typeof(AdminOrderUpscalesController).GetCustomAttribute<AuthorizeAttribute>();
    Check(authorize?.Roles == "Admin", "Prepare and download both require admin role");

    // Replacing a design invalidates previous derivatives even at the same path.
    using (var replacement = new Image<Rgba32>(4, 2, new Rgba32(50, 60, 70)))
        await replacement.SaveAsPngAsync(root + "/original.png");
    Check(await service.GetCachedPathAsync("original.png", 2, default) is null, "Replacement original invalidates old cache by content hash");
    fixture.Complete = false;
    fixture.Error = true;
    Check(!await service.PrepareAsync("original.png", 2, "fixture-key", default), "Changed file starts its own queue job");
    await Throws(() => service.PrepareAsync("original.png", 2, "fixture-key", default), 502, "Failed queue result surfaces useful error");
    var submissions = fixture.Submissions;
    await Throws(() => Service().PrepareAsync("original.png", 2, "fixture-key", default), 502, "Failed queue handle survives restart without new charge");
    Check(fixture.Submissions == submissions, "Terminal errors do not trigger automatic paid resubmission");
    fixture.Error = false;
    fixture.SubmitTimeout = true;
    try { await service.PrepareAsync("original.png", 4, "fixture-key", default); throw new Exception("Timeout not raised"); }
    catch (TaskCanceledException) { }
    submissions = fixture.Submissions;
    await Throws(() => Service().PrepareAsync("original.png", 4, "fixture-key", default), 502, "Ambiguous submit timeout is preserved for reconciliation");
    Check(fixture.Submissions == submissions, "Ambiguous submission cannot charge twice on retry");
    Console.WriteLine("All offline upscale checks passed. No live fal calls made.");
}
finally { Directory.Delete(root, recursive: true); }

sealed class FalFixture(byte[] png) : HttpMessageHandler, IHttpClientFactory
{
    public int Calls;
    public int Submissions;
    public bool Complete, BrokenDownload, Error, SubmitTimeout;
    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;
        var path = request.RequestUri!.AbsolutePath;
        if (request.RequestUri.Host == "queue.fal.run")
        {
            if (request.Headers.Authorization?.ToString() != "Key fixture-key") throw new Exception("Incorrect authentication");
            if (request.Method == HttpMethod.Post)
            {
                Submissions++;
                using var input = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                if (path != "/" + FalOrderUpscaleService.Model ||
                    input.RootElement.GetProperty("upscale_mode").GetString() != "factor" ||
                    input.RootElement.GetProperty("upscale_factor").GetInt32() is not (2 or 4) ||
                    input.RootElement.GetProperty("output_format").GetString() != "png" ||
                    !input.RootElement.GetProperty("image_url").GetString()!.StartsWith("data:image/png;base64,"))
                    throw new Exception("Incorrect model/payload");
                if (SubmitTimeout) throw new TaskCanceledException("fixture submit timeout");
                return Json(new { request_id = $"job-{Submissions}",
                    status_url = $"https://queue.fal.run/fal-ai/seedvr/requests/job-{Submissions}/status",
                    response_url = $"https://queue.fal.run/fal-ai/seedvr/requests/job-{Submissions}" });
            }
            if (path.EndsWith("/status"))
                return Error ? Json(new { status = "COMPLETED", error = "fixture error" })
                    : Json(new { status = Complete ? "COMPLETED" : "IN_PROGRESS" });
            return Json(new { image = new { url = "https://v3.fal.media/fixture.png" } });
        }
        if (request.RequestUri.Host != "v3.fal.media" || request.Headers.Authorization is not null)
            throw new Exception("Unexpected request or credential leaked to media host");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(BrokenDownload ? [1, 2, 3] : png) };
    }
    static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
}
