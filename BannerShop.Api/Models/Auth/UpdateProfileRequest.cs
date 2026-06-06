using System.ComponentModel.DataAnnotations;

namespace BannerShop.Api.Models.Auth;

public class UpdateProfileRequest
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Phone { get; set; }
}
