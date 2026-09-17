// Standalone controller/service smoke. SQLite is local/in-memory; no AI, production or project suite.
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using BannerShop.Api.Controllers;
using BannerShop.Api.Models.DesignRequests;
using BannerShop.Api.Services.BannerBuilder;
using BannerShop.Api.Services.DesignRequests;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
using var db = new BannerShopDbContext(new DbContextOptionsBuilder<BannerShopDbContext>().UseSqlite(connection).Options);
await db.Database.EnsureCreatedAsync();
db.Users.AddRange(new User { Id = 305, Email = "guest305@example.invalid" }, new User { Id = 306, Email = "other306@example.invalid" });
db.BannerDesigns.Add(new BannerDesign { Id = 305, StoragePath = "portrait.png" });
var row = new DesignRequest { Id = 305, BannerTemplateId = 1, Mode = DesignRequestMode.Ai, Status = DesignRequestStatus.InProgress,
    PersonName = "Guest 305", TextContent = "Happy birthday", ThemeDescription = "Stars", UploadedPhotoPath = "portrait.png" };
db.DesignRequests.Add(row);
await db.SaveChangesAsync();
var storage = new BannerFileStorage(Options.Create(new FileStorageOptions { LocalRoot = "/tmp/bannersh-305-smoke", PublicBaseUrl = "/files" }));
var service = new DesignRequestService(db, null!, null!, storage, null!, null!, null!, null!, NullLogger<DesignRequestService>.Instance);
var creation = new Mock<IDesignRequestService>();
creation.Setup(s => s.CreateAiRequestAsync(null, It.IsAny<string?>(), It.IsAny<CreateAiDesignRequestDto>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(CreateAiResult.Ok(305, true, 0));
var protection = new EphemeralDataProtectionProvider();
DesignRequestsController Controller(IDesignRequestService svc, string? cookie = null, int? user = null) {
    var ctx = new DefaultHttpContext();
    if (cookie != null) ctx.Request.Headers.Cookie = cookie;
    if (user != null) ctx.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString()!)], "smoke"));
    return new DesignRequestsController(svc, protection) { ControllerContext = new ControllerContext { HttpContext = ctx } };
}
void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
Check(typeof(DesignRequestsController).GetMethod("Get")!.IsDefined(typeof(AllowAnonymousAttribute), true), "guest GET bypasses class auth middleware");
Check(!typeof(DesignRequestsController).GetMethod("Approve")!.IsDefined(typeof(AllowAnonymousAttribute), true), "approval retains authentication middleware");
var expired = protection.CreateProtector("BannerShop.GuestDesign.v1").ToTimeLimitedDataProtector().Protect("305", TimeSpan.FromSeconds(-1));
Check(await Controller(service, "bannershop_guest_305=" + expired).Get(305, default) is NotFoundResult, "expired guest cookie denied");
var creator = Controller(creation.Object);
await creator.CreateAi(new CreateAiDesignRequestDto(), default);
var cookie = creator.Response.Headers.SetCookie.ToString().Split(';')[0];
Check(cookie.StartsWith("bannershop_guest_305="), "guest creation issues an ownership cookie");
Check(creator.Response.Headers.SetCookie.ToString().Contains("httponly", StringComparison.OrdinalIgnoreCase), "ownership cookie is HttpOnly");
var guest = Controller(service, cookie);
Check(await guest.Get(305, default) is OkObjectResult { Value: DesignRequestDetailDto { Status: "InProgress" } }, "guest polls pending banner");
Check(await Controller(service).Get(305, default) is NotFoundResult, "id alone cannot read guest data");
Check(await Controller(service, user: 306).Get(305, default) is NotFoundResult, "another account cannot enumerate guest data");
Check(await Controller(service, "bannershop_guest_305=not-base64!").Get(305, default) is NotFoundResult, "malformed cookie denied");
Check(await Controller(service, cookie + "tampered").Get(305, default) is NotFoundResult, "tampered cookie denied");
Check(await guest.Get(306, default) is NotFoundResult, "cookie cannot access another request");
Check(await guest.Approve(305, null, default) is UnauthorizedResult, "guest approval remains gated");
Check(await guest.Claim(305, default) is UnauthorizedResult, "claim requires account");
Check(await Controller(service, user: 305).Claim(305, default) is NotFoundResult, "account alone cannot claim by id");
row.Status = DesignRequestStatus.AwaitingApproval;
row.FinalCroppedStoragePath = "guest-preview.png";
await db.SaveChangesAsync();
Check(await guest.Get(305, default) is OkObjectResult { Value: DesignRequestDetailDto { PreviewUrl: "/files/guest-preview.png" } }, "guest sees completed preview without registration");
var owner = Controller(service, cookie, 305);
Check(await owner.Claim(305, default) is OkObjectResult { Value: DesignRequestDetailDto { UserId: 305, PersonName: "Guest 305" } }, "sign-up claims same request and form");
Check(await owner.Claim(305, default) is OkObjectResult, "claim is idempotent");
Check(await Controller(service, cookie, 306).Claim(305, default) is NotFoundResult, "second account cannot steal claimed request");
Check(await guest.Get(305, default) is NotFoundResult, "guest access ends after claim");
Check(await Controller(service, user: 305).Get(305, default) is OkObjectResult, "owner reads without guest cookie");
Check(await db.BannerDesigns.AsNoTracking().AnyAsync(d => d.Id == 305 && d.UserId == 305), "portrait transferred for reuse in restored form");
Check(await db.DesignRequests.CountAsync() == 1 && await db.Orders.CountAsync() == 0, "claim makes no duplicate request or premature order");
