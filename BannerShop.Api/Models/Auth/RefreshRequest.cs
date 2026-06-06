using System.ComponentModel.DataAnnotations;

namespace BannerShop.Api.Models.Auth;

public class RefreshRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}
