namespace BannerShop.Api.Models.Auth;

public class AuthResponseDto
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public UserResponseDto User { get; set; } = null!;
}
