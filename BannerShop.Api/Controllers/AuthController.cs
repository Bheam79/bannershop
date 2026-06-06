using System.Security.Claims;
using BannerShop.Api.Models.Auth;
using BannerShop.Api.Services;
using BannerShop.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BannerShop.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth) => _auth = auth;

    // ── POST /api/auth/register ───────────────────────────────────────────────
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        var result = await _auth.RegisterAsync(req.Email, req.Password, req.Name, req.Phone);
        if (!result.Success)
            return BadRequest(new { error = result.Error });
        return Ok(ToAuthResponse(result));
    }

    // ── POST /api/auth/login ──────────────────────────────────────────────────
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var result = await _auth.LoginAsync(req.Email, req.Password);
        if (!result.Success)
            return Unauthorized(new { error = result.Error });
        return Ok(ToAuthResponse(result));
    }

    // ── POST /api/auth/refresh ────────────────────────────────────────────────
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest req)
    {
        var result = await _auth.RefreshAsync(req.RefreshToken);
        if (!result.Success)
            return Unauthorized(new { error = result.Error });
        return Ok(ToAuthResponse(result));
    }

    // ── POST /api/auth/logout ─────────────────────────────────────────────────
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest req)
    {
        await _auth.LogoutAsync(req.RefreshToken);
        return NoContent();
    }

    // ── GET /api/auth/me ──────────────────────────────────────────────────────
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        var user = await _auth.GetUserAsync(userId);
        if (user == null) return NotFound();
        return Ok(ToUserResponse(user));
    }

    // ── PUT /api/auth/me ──────────────────────────────────────────────────────
    [Authorize]
    [HttpPut("me")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest req)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        var user = await _auth.UpdateProfileAsync(userId, req.Name, req.Phone);
        if (user == null) return NotFound();
        return Ok(ToUserResponse(user));
    }

    // ── POST /api/auth/change-password ────────────────────────────────────────
    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();
        var ok = await _auth.ChangePasswordAsync(userId, req.CurrentPassword, req.NewPassword);
        if (!ok)
            return BadRequest(new { error = "Current password is incorrect." });
        return NoContent();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private int GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : 0;
    }

    private static AuthResponseDto ToAuthResponse(AuthResult result) => new()
    {
        AccessToken = result.AccessToken!,
        RefreshToken = result.RefreshToken!,
        User = ToUserResponse(result.User!)
    };

    private static UserResponseDto ToUserResponse(User user) => new()
    {
        Id = user.Id,
        Email = user.Email,
        Name = user.Name,
        Phone = user.Phone,
        Role = user.Role.ToString()
    };
}
