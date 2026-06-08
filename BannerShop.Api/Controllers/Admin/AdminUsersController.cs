using BannerShop.Api.Models.Admin;
using BannerShop.Api.Services.AiCredits;
using BannerShop.Core.Enums;
using BannerShop.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Controllers.Admin;

/// <summary>
/// Admin endpoints for browsing registered users and managing their AI credits
/// (BANNERSH-86 — admin user list + manual credit grants).
/// All routes require Admin role.
/// </summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = "Admin")]
public class AdminUsersController : ControllerBase
{
    private const int MaxPageSize = 100;
    private const int RecentTransactionsLimit = 50;

    private readonly BannerShopDbContext _db;
    private readonly IAiCreditService _credits;
    private readonly ILogger<AdminUsersController> _log;

    public AdminUsersController(
        BannerShopDbContext db,
        IAiCreditService credits,
        ILogger<AdminUsersController> log)
    {
        _db = db;
        _credits = credits;
        _log = log;
    }

    // ── GET /api/admin/users ─────────────────────────────────────────────────
    /// <summary>
    /// Paginated user list with optional case-insensitive search across id, email,
    /// name and phone. Sorted newest-first.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;

        var q = _db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            // EF Core translates Contains to LIKE — case-insensitivity depends on
            // the column collation; MariaDB defaults (utf8mb4_*_ci) handle that.
            if (int.TryParse(s, out var idGuess))
            {
                q = q.Where(u =>
                    u.Id == idGuess ||
                    u.Email.Contains(s) ||
                    u.Name.Contains(s) ||
                    (u.Phone != null && u.Phone.Contains(s)));
            }
            else
            {
                q = q.Where(u =>
                    u.Email.Contains(s) ||
                    u.Name.Contains(s) ||
                    (u.Phone != null && u.Phone.Contains(s)));
            }
        }

        var totalCount = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(u => u.CreatedAt)
            .ThenByDescending(u => u.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new AdminUserListItem
            {
                Id = u.Id,
                Email = u.Email,
                Name = u.Name,
                Phone = u.Phone,
                Role = u.Role.ToString(),
                AiCreditsRemaining = u.AiCreditsRemaining,
                HasUsedFreeAiGeneration = u.HasUsedFreeAiGeneration,
                OrderCount = u.Orders.Count,
                CreatedAt = u.CreatedAt
            })
            .ToListAsync(ct);

        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        return Ok(new AdminUsersPage
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages
        });
    }

    // ── GET /api/admin/users/{id} ────────────────────────────────────────────
    /// <summary>
    /// User detail including aggregate counts and the most recent AI credit
    /// ledger rows (consume + grant history).
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var user = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new
            {
                u.Id, u.Email, u.Name, u.Phone, u.Role,
                u.AiCreditsRemaining, u.HasUsedFreeAiGeneration, u.CreatedAt,
                OrderCount = u.Orders.Count
            })
            .FirstOrDefaultAsync(ct);

        if (user is null) return NotFound();

        var designRequestCount = await _db.DesignRequests
            .AsNoTracking()
            .CountAsync(d => d.UserId == id, ct);

        var transactions = await _db.AiCreditTransactions
            .AsNoTracking()
            .Where(t => t.UserId == id)
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Take(RecentTransactionsLimit)
            .Select(t => new AdminAiCreditTransactionDto
            {
                Id = t.Id,
                Amount = t.Amount,
                Reason = t.Reason.ToString(),
                ReferenceId = t.ReferenceId,
                IpAddress = t.IpAddress,
                CreatedAt = t.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(new AdminUserDetail
        {
            Id = user.Id,
            Email = user.Email,
            Name = user.Name,
            Phone = user.Phone,
            Role = user.Role.ToString(),
            AiCreditsRemaining = user.AiCreditsRemaining,
            HasUsedFreeAiGeneration = user.HasUsedFreeAiGeneration,
            OrderCount = user.OrderCount,
            DesignRequestCount = designRequestCount,
            CreatedAt = user.CreatedAt,
            RecentCreditTransactions = transactions
        });
    }

    // ── POST /api/admin/users/{id}/grant-credits ─────────────────────────────
    /// <summary>
    /// Manually grants the user free AI credits. The transaction is recorded in
    /// <c>AiCreditTransactions</c> with reason <see cref="CreditReason.AdminGrant"/>
    /// and a NULL <c>ReferenceId</c> — i.e. a "null payment row" so the grant can
    /// be distinguished from purchased / order-activation credits in the ledger.
    /// </summary>
    [HttpPost("{id:int}/grant-credits")]
    public async Task<IActionResult> GrantCredits(
        int id,
        [FromBody] GrantCreditsRequest req,
        CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var exists = await _db.Users.AsNoTracking().AnyAsync(u => u.Id == id, ct);
        if (!exists) return NotFound(new { error = "User not found." });

        // GrantAsync handles the User.AiCreditsRemaining increment + ledger insert.
        // ReferenceId is left null per BANNERSH-86 ("null payment row").
        await _credits.GrantAsync(id, req.Amount, CreditReason.AdminGrant, referenceId: null, ct);

        _log.LogInformation(
            "Admin {AdminId} granted {Count} AI credits to user {UserId}.",
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            req.Amount, id);

        // Return the refreshed detail so the UI can update without an extra round-trip.
        return await Get(id, ct);
    }
}
