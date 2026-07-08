using BannerShop.Api.Services.SystemSettings;
using BannerShop.Core.Entities;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace BannerShop.Tests;

/// <summary>
/// Unit tests for SystemSettingsService using an in-memory EF Core database.
/// </summary>
public class SystemSettingsServiceTests
{
    private static SystemSettingsService CreateService(out BannerShop.Infrastructure.Data.BannerShopDbContext db)
        => CreateService(out db, out _);

    private static SystemSettingsService CreateService(
        out BannerShop.Infrastructure.Data.BannerShopDbContext db,
        out IMemoryCache cache)
    {
        db = DbHelper.CreateInMemory();
        cache = new MemoryCache(new MemoryCacheOptions());
        return new SystemSettingsService(db, cache);
    }

    // ── GetValueAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetValueAsync_ExistingKey_ReturnsValue()
    {
        var svc = CreateService(out var db);
        db.SystemSettings.Add(new SystemSetting { Key = "test_key", Value = "hello" });
        await db.SaveChangesAsync();

        var result = await svc.GetValueAsync("test_key");

        result.Should().Be("hello");
    }

    [Fact]
    public async Task GetValueAsync_MissingKey_ReturnsNull()
    {
        var svc = CreateService(out _);

        var result = await svc.GetValueAsync("nonexistent");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetValueAsync_WhitespaceValue_ReturnsNull()
    {
        var svc = CreateService(out var db);
        db.SystemSettings.Add(new SystemSetting { Key = "blank_key", Value = "   " });
        await db.SaveChangesAsync();

        var result = await svc.GetValueAsync("blank_key");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetValueAsync_EmptyStringValue_ReturnsNull()
    {
        var svc = CreateService(out var db);
        db.SystemSettings.Add(new SystemSetting { Key = "empty_key", Value = "" });
        await db.SaveChangesAsync();

        var result = await svc.GetValueAsync("empty_key");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetValueAsync_CachesResult_DoesNotReflectDirectDbMutationWithinTtl()
    {
        var svc = CreateService(out var db);
        db.SystemSettings.Add(new SystemSetting { Key = "cached_key", Value = "v1" });
        await db.SaveChangesAsync();

        var first = await svc.GetValueAsync("cached_key");
        first.Should().Be("v1");

        // Mutate the row directly (bypassing SetValueAsync, which invalidates the cache)
        // to prove the second read is served from cache rather than re-querying the DB.
        var row = db.SystemSettings.First(s => s.Key == "cached_key");
        row.Value = "v2";
        await db.SaveChangesAsync();

        var second = await svc.GetValueAsync("cached_key");
        second.Should().Be("v1");
    }

    [Fact]
    public async Task GetValueAsync_CachesMissingKey_DoesNotReflectLaterInsertWithinTtl()
    {
        var svc = CreateService(out var db);

        var first = await svc.GetValueAsync("not_yet_set");
        first.Should().BeNull();

        db.SystemSettings.Add(new SystemSetting { Key = "not_yet_set", Value = "now_set" });
        await db.SaveChangesAsync();

        var second = await svc.GetValueAsync("not_yet_set");
        second.Should().BeNull("the earlier cache miss should itself have been cached");
    }

    // ── SetValueAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SetValueAsync_NewKey_InsertsRow()
    {
        var svc = CreateService(out var db);

        await svc.SetValueAsync("new_key", "new_value");

        var row = db.SystemSettings.FirstOrDefault(s => s.Key == "new_key");
        row.Should().NotBeNull();
        row!.Value.Should().Be("new_value");
    }

    [Fact]
    public async Task SetValueAsync_ExistingKey_UpdatesRow()
    {
        var svc = CreateService(out var db);
        db.SystemSettings.Add(new SystemSetting { Key = "update_key", Value = "old" });
        await db.SaveChangesAsync();

        await svc.SetValueAsync("update_key", "new");

        var row = db.SystemSettings.FirstOrDefault(s => s.Key == "update_key");
        row!.Value.Should().Be("new");
    }

    [Fact]
    public async Task SetValueAsync_InvalidatesCacheForThatKey()
    {
        var svc = CreateService(out var db);
        db.SystemSettings.Add(new SystemSetting { Key = "k", Value = "old" });
        await db.SaveChangesAsync();
        (await svc.GetValueAsync("k")).Should().Be("old"); // warm the cache

        await svc.SetValueAsync("k", "new");

        var result = await svc.GetValueAsync("k");
        result.Should().Be("new");
    }

    // ── GetAllAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_ReturnsAllSettings()
    {
        var svc = CreateService(out var db);
        db.SystemSettings.AddRange(
            new SystemSetting { Key = "key1", Label = "Label 1", Value = "v1", IsSensitive = false },
            new SystemSetting { Key = "key2", Label = "Label 2", Value = "v2", IsSensitive = true }
        );
        await db.SaveChangesAsync();

        var result = await svc.GetAllAsync();

        result.Should().HaveCount(2);
        result.Should().ContainSingle(s => s.Key == "key1" && s.Value == "v1");
        result.Should().ContainSingle(s => s.Key == "key2" && s.IsSensitive);
    }

    [Fact]
    public async Task GetAllAsync_Empty_ReturnsEmptyList()
    {
        var svc = CreateService(out _);

        var result = await svc.GetAllAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_NullLabel_UsesKeyAsLabel()
    {
        var svc = CreateService(out var db);
        db.SystemSettings.Add(new SystemSetting { Key = "mykey", Label = null, Value = "val" });
        await db.SaveChangesAsync();

        var result = await svc.GetAllAsync();

        result.Should().ContainSingle(s => s.Label == "mykey");
    }

    [Fact]
    public async Task GetAllAsync_BypassesCache_ReflectsLatestDbState()
    {
        var svc = CreateService(out var db);
        db.SystemSettings.Add(new SystemSetting { Key = "k", Value = "v1" });
        await db.SaveChangesAsync();
        await svc.GetValueAsync("k"); // warm the per-key cache

        var row = db.SystemSettings.First(s => s.Key == "k");
        row.Value = "v2";
        await db.SaveChangesAsync();

        var result = await svc.GetAllAsync();
        result.Should().ContainSingle(s => s.Key == "k" && s.Value == "v2");
    }
}
