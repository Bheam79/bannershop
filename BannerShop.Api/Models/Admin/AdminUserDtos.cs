using System.ComponentModel.DataAnnotations;

namespace BannerShop.Api.Models.Admin;

/// <summary>Row in the admin user-list table.</summary>
public class AdminUserListItem
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Role { get; set; } = string.Empty;
    public int AiCreditsRemaining { get; set; }
    public bool HasUsedFreeAiGeneration { get; set; }
    public int OrderCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Paged response for the admin user list.</summary>
public class AdminUsersPage
{
    public List<AdminUserListItem> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

/// <summary>Audit-log row in the admin user detail view.</summary>
public class AdminAiCreditTransactionDto
{
    public int Id { get; set; }
    public int Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Detailed admin view of a single user.</summary>
public class AdminUserDetail
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Role { get; set; } = string.Empty;
    public int AiCreditsRemaining { get; set; }
    public bool HasUsedFreeAiGeneration { get; set; }
    public int OrderCount { get; set; }
    public int DesignRequestCount { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Most recent credit-ledger rows for the user (newest first).</summary>
    public List<AdminAiCreditTransactionDto> RecentCreditTransactions { get; set; } = new();
}

/// <summary>Request body for POST /api/admin/users/{id}/grant-credits.</summary>
public class GrantCreditsRequest
{
    /// <summary>Number of credits to add to the user's pool (must be >= 1).</summary>
    [Range(1, 10_000, ErrorMessage = "Amount must be between 1 and 10000.")]
    public int Amount { get; set; }
}
