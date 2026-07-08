using BannerShop.Core.Entities;
using BannerShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BannerShop.Api.Services.SystemSettings;

/// <summary>
/// Database-backed implementation of <see cref="ISystemSettingsService"/>.
/// <see cref="GetValueAsync"/> is cached for <see cref="CacheTtl"/> (per-key,
/// including cache misses) since these values change at most a few times a
/// year but are read on every Stripe/OpenAI call and on every anonymous
/// page load (GET /api/config/stripe). <see cref="SetValueAsync"/> evicts the
/// entry it just wrote so admin edits take effect immediately for that key.
/// <see cref="GetAllAsync"/> deliberately bypasses the cache — it only backs
/// the /admin/settings editor, which must always show the current DB state.
/// </summary>
public sealed class SystemSettingsService : ISystemSettingsService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private readonly BannerShopDbContext _db;
    private readonly IMemoryCache _cache;

    public SystemSettingsService(BannerShopDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<string?> GetValueAsync(string key, CancellationToken ct = default)
    {
        var cacheKey = CacheKey(key);
        if (_cache.TryGetValue<string?>(cacheKey, out var cached))
            return cached;

        var row = await _db.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key, ct);

        var val = row?.Value;
        var result = string.IsNullOrWhiteSpace(val) ? null : val;

        _cache.Set(cacheKey, result, CacheTtl);
        return result;
    }

    public async Task SetValueAsync(string key, string value, CancellationToken ct = default)
    {
        var row = await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null)
        {
            row = new SystemSetting { Key = key, Value = value };
            _db.SystemSettings.Add(row);
        }
        else
        {
            row.Value = value;
        }
        await _db.SaveChangesAsync(ct);
        _cache.Remove(CacheKey(key));
    }

    private static string CacheKey(string key) => $"system_settings:{key}";

    public async Task<IReadOnlyList<SystemSettingDto>> GetAllAsync(CancellationToken ct = default)
    {
        var rows = await _db.SystemSettings
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .ToListAsync(ct);

        return rows
            .Select(s => new SystemSettingDto(s.Id, s.Key, s.Label ?? s.Key, s.IsSensitive, s.Value))
            .ToList();
    }
}
