using System.Reflection;
using BannerShop.Core.Entities;
using BannerShop.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace BannerShop.Api.Controllers;

/// <summary>
/// Application feature provider that removes <see cref="TestOnlyController"/> from
/// MVC's controller discovery so its routes don't exist at all outside Development.
/// Registered from Program.cs only when <c>!builder.Environment.IsDevelopment()</c>.
/// </summary>
internal sealed class TestOnlyControllerExcludingFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        var ti = typeof(TestOnlyController).GetTypeInfo();
        feature.Controllers.Remove(ti);
    }
}

/// <summary>
/// Test-only endpoints used by the Playwright E2E suite to pre-seed state
/// that would otherwise require racey "make N requests" setups.
///
/// IMPORTANT: this controller is only registered when the host environment
/// is Development (see Program.cs). It is never exposed in Production —
/// in any other environment the routes return 404 because the controller
/// class is filtered out before MVC discovers it.
/// </summary>
[ApiController]
[Route("api/test")]
public class TestOnlyController : ControllerBase
{
    private readonly BannerShopDbContext _db;

    public TestOnlyController(BannerShopDbContext db)
    {
        _db = db;
    }

    public sealed class SeedIpAiUsageRequest
    {
        public string IpAddress { get; set; } = string.Empty;
    }

    /// <summary>
    /// Insert a single <see cref="IpAiUsage"/> row for the given IP at <c>UtcNow</c>.
    /// Used by ai-banner-anonymous E2E specs (BANNERSH-79) to put a clean test IP
    /// into the "already used the free generation" state without running a full
    /// generation through the AI pipeline first.
    /// </summary>
    [HttpPost("seed-ip-ai-usage")]
    public async Task<IActionResult> SeedIpAiUsage([FromBody] SeedIpAiUsageRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.IpAddress))
        {
            return BadRequest(new { error = "ipAddress is required" });
        }

        _db.IpAiUsages.Add(new IpAiUsage
        {
            IpAddress = request.IpAddress.Trim(),
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }
}
