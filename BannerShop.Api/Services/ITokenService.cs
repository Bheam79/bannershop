using BannerShop.Core.Entities;

namespace BannerShop.Api.Services;

public interface ITokenService
{
    string GenerateAccessToken(User user);
    string GenerateRefreshToken();
}
