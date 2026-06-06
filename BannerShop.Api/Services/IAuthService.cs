using BannerShop.Core.Entities;

namespace BannerShop.Api.Services;

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(string email, string password, string name, string? phone);
    Task<AuthResult> LoginAsync(string email, string password);
    Task<AuthResult> RefreshAsync(string refreshToken);
    Task LogoutAsync(string refreshToken);
    Task<User?> GetUserAsync(int userId);
    Task<User?> UpdateProfileAsync(int userId, string name, string? phone);
    Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword);
}
