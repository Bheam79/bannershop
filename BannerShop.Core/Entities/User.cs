using BannerShop.Core.Enums;

namespace BannerShop.Core.Entities;

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public UserRole Role { get; set; } = UserRole.Customer;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ── AI credit pool (BANNERSH-65) ──────────────────────────────────────────
    /// <summary>Current balance of purchased / granted AI generation credits.</summary>
    public int AiCreditsRemaining { get; set; } = 0;

    /// <summary>
    /// True once the user has consumed their single free AI generation.
    /// Guards against claiming the free generation a second time.
    /// </summary>
    public bool HasUsedFreeAiGeneration { get; set; } = false;

    // Navigation
    public ICollection<Address> Addresses { get; set; } = new List<Address>();
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<AiCreditTransaction> AiCreditTransactions { get; set; } = new List<AiCreditTransaction>();
}
