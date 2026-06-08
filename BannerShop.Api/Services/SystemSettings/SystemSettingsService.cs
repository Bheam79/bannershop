using BannerShop.Core.Entities;
using BannerShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BannerShop.Api.Services.SystemSettings;

/// <summary>
/// Database-backed implementation of <see cref="ISystemSettingsService"/>.
/// Each call goes to the database; the caller (e.g. <c>OpenAiImageService</c>)
/// is responsible for any caching it needs.
/// </summary>
public sealed class SystemSettingsService : ISystemSettingsService
{
    private readonly BannerShopDbContext _db;

    public SystemSettingsService(BannerShopDbContext db) => _db = db;

    public async Task<string?> GetValueAsync(string key, CancellationToken ct = default)
    {
        var row = await _db.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key, ct);

        var val = row?.Value;
        return string.IsNullOrWhiteSpace(val) ? null : val;
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
    }

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
