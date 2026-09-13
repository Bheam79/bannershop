using BannerShop.Api.Services.DesignRequests.Cli;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BannerShop.Api.Controllers.Admin;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/settings/image-cli/{provider}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminImageCliController(ImageCliRuntime runtime) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> Status(string provider, CancellationToken ct) =>
        ImageCliRuntime.IsProvider(provider) ? Ok(await runtime.StatusAsync(provider, ct)) : NotFound();

    [HttpPost("start")]
    public async Task<IActionResult> Start(string provider, CancellationToken ct)
    {
        if (!ImageCliRuntime.IsProvider(provider)) return NotFound();
        runtime.StartLogin(provider);
        return Ok(await runtime.StatusAsync(provider, ct));
    }

    [HttpPost("cancel")]
    public IActionResult Cancel(string provider)
    {
        if (!ImageCliRuntime.IsProvider(provider)) return NotFound();
        runtime.CancelLogin(provider);
        return NoContent();
    }
}
