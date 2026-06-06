using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Services;

public class AuthService : IAuthService
{
    private readonly BannerShopDbContext _db;
    private readonly ITokenService _tokenService;
    private readonly IConfiguration _config;

    public AuthService(BannerShopDbContext db, ITokenService tokenService, IConfiguration config)
    {
        _db = db;
        _tokenService = tokenService;
        _config = config;
    }

    public async Task<AuthResult> RegisterAsync(string email, string password, string name, string? phone)
    {
        var normalised = email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email == normalised))
            return Fail("Email is already registered.");

        var user = new User
        {
            Email = normalised,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Name = name.Trim(),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            Role = UserRole.Customer
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return await IssueTokensAsync(user);
    }

    public async Task<AuthResult> LoginAsync(string email, string password)
    {
        var normalised = email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == normalised);
        if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            return Fail("Invalid email or password.");

        return await IssueTokensAsync(user);
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken)
    {
        var stored = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == refreshToken && !t.IsRevoked && t.ExpiresAt > DateTime.UtcNow);

        if (stored == null)
            return Fail("Invalid or expired refresh token.");

        // Rotate: revoke old token
        stored.IsRevoked = true;

        var result = await IssueTokensAsync(stored.User);
        return result;
    }

    public async Task LogoutAsync(string refreshToken)
    {
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.Token == refreshToken);
        if (stored != null)
        {
            stored.IsRevoked = true;
            await _db.SaveChangesAsync();
        }
    }

    public Task<User?> GetUserAsync(int userId)
        => _db.Users.FindAsync(userId).AsTask()!;

    public async Task<User?> UpdateProfileAsync(int userId, string name, string? phone)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return null;
        user.Name = name.Trim();
        user.Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        await _db.SaveChangesAsync();
        return user;
    }

    public async Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null || !BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
            return false;
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await _db.SaveChangesAsync();
        return true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<AuthResult> IssueTokensAsync(User user)
    {
        var expiryDays = int.TryParse(_config["Jwt:RefreshTokenExpiryDays"], out var d) ? d : 30;
        var rawToken = _tokenService.GenerateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            Token = rawToken,
            ExpiresAt = DateTime.UtcNow.AddDays(expiryDays)
        });
        await _db.SaveChangesAsync();

        return new AuthResult
        {
            Success = true,
            AccessToken = _tokenService.GenerateAccessToken(user),
            RefreshToken = rawToken,
            User = user
        };
    }

    private static AuthResult Fail(string error) => new() { Success = false, Error = error };
}
