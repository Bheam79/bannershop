using BannerShop.Core.Entities;

namespace BannerShop.Api.Services;

public class AuthResult
{
    public bool Success { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public User? User { get; set; }
    public string? Error { get; set; }
}
