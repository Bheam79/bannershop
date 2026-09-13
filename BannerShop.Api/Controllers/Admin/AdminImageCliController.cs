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
    public async Task<IActionResult> Start(string provider, CancellationToken ct, [FromQuery] string method = "device")
    {
        if (!ImageCliRuntime.IsProvider(provider)) return NotFound();
        if (method is not ("device" or "oauth") || (method == "oauth" && provider != "grok"))
            return BadRequest(new { error = "Ugyldig innloggingsmetode." });
        runtime.StartLogin(provider, browserOAuth: method == "oauth");
        return Ok(await runtime.StatusAsync(provider, ct));
    }

    [HttpPost("complete")]
    [RequestSizeLimit(16384)]
    public async Task<IActionResult> Complete(string provider, [FromBody] CompleteImageCliLoginRequest request, CancellationToken ct)
    {
        if (!ImageCliRuntime.IsProvider(provider)) return NotFound();
        try
        {
            await runtime.CompleteBrowserLoginAsync(provider, request.Code, ct);
            return Ok(await runtime.StatusAsync(provider, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("cancel")]
    public IActionResult Cancel(string provider)
    {
        if (!ImageCliRuntime.IsProvider(provider)) return NotFound();
        runtime.CancelLogin(provider);
        return NoContent();
    }
}

public sealed record CompleteImageCliLoginRequest(string? Code);
