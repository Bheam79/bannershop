using BannerShop.Core.Entities;
using BannerShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySqlConnector;

const string previous = "20261007084113_AddFalOrderUpscaleKey";
const string migration = "20261007151246_AddAdminOrderSmsKey";
const string smsKey = "admin_order_sms_key";
var expectCollision = args.Contains("--expect-collision");
var server = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SETTINGS_SMOKE_MYSQL")
    ?? throw new InvalidOperationException("Set SETTINGS_SMOKE_MYSQL to a disposable MariaDB server (CREATE/DROP DATABASE required)."));
server.Database = "";
await using var admin = new MySqlConnection(server.ConnectionString);
await admin.OpenAsync();

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    Console.WriteLine("PASS: " + message);
}

async Task Run(string label, Func<BannerShopDbContext, Task> check)
{
    var name = "bannersh316_" + Guid.NewGuid().ToString("N");
    await using (var create = new MySqlCommand($"CREATE DATABASE `{name}`", admin))
        await create.ExecuteNonQueryAsync();
    try
    {
        var connection = new MySqlConnectionStringBuilder(server.ConnectionString) { Database = name };
        await using var db = new BannerShopDbContext(new DbContextOptionsBuilder<BannerShopDbContext>()
            .UseMySql(connection.ConnectionString, new MariaDbServerVersion(new Version(11, 0, 0)),
                options => options.EnableRetryOnFailure(3)).Options);
        Console.WriteLine("Scenario: " + label);
        await check(db);
    }
    finally
    {
        await using var drop = new MySqlCommand($"DROP DATABASE `{name}`", admin);
        await drop.ExecuteNonQueryAsync();
    }
}

async Task<List<string>> Settings(BannerShopDbContext db) => (await db.SystemSettings.AsNoTracking()
    .Where(s => s.Key != smsKey).OrderBy(s => s.Id).ToListAsync())
    .Select(s => $"{s.Id}|{s.Key}|{s.Value}|{s.Label}|{s.IsSensitive}").ToList();

async Task AddCliCredentials(BannerShopDbContext db)
{
    // Same auto-increment inserts as ImageCliRuntime.StoreCredentialAsync after OAuth login.
    var codex = new SystemSetting { Key = "codex_image_cli_credentials", Value = "fixture-codex", IsSensitive = true };
    db.SystemSettings.Add(codex);
    await db.SaveChangesAsync();
    Check(codex.Id == 20, "runtime CLI credential occupies ID 20 before the upgrade");
    db.SystemSettings.Add(new SystemSetting { Key = "grok_image_cli_credentials", Value = "fixture-grok", IsSensitive = true });
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();
}

await Run("upgrade with CLI credentials occupying the seeded ID (original reproduction)", async db =>
{
    await db.GetService<IMigrator>().MigrateAsync(previous);
    await AddCliCredentials(db);
    var before = await Settings(db);
    if (expectCollision)
    {
        try
        {
            db.Database.Migrate(); // Same call as production Program.cs startup.
            throw new InvalidOperationException("Expected the original duplicate-ID startup failure.");
        }
        catch (MySqlException ex) when (ex.Number == 1062 && ex.Message.Contains("'20'"))
        {
            Console.WriteLine("REPRODUCED: " + ex.Message);
            Check(!(await db.Database.GetAppliedMigrationsAsync()).Contains(migration), "failed migration remains pending");
            Check(before.SequenceEqual(await Settings(db)), "failed upgrade leaves credentials intact");
        }
        return;
    }

    db.Database.Migrate();
    var sms = await db.SystemSettings.AsNoTracking().SingleAsync(s => s.Key == smsKey);
    Check(sms.Id > 21 && sms.IsSensitive && !string.IsNullOrWhiteSpace(sms.Value), "same startup migration succeeds with an allocated ID and the shipped SMS default");
    Check((await db.Database.GetAppliedMigrationsAsync()).Contains(migration), "upgrade recorded in migration history");
    Check(before.SequenceEqual(await Settings(db)), "all pre-existing settings and credentials preserved");
    db.Database.Migrate();
    Check(await db.SystemSettings.CountAsync(s => s.Key == smsKey) == 1, "second startup does not duplicate SMS setting");
    await db.GetService<IMigrator>().MigrateAsync(previous);
    Check(!await db.SystemSettings.AnyAsync(s => s.Key == smsKey), "rollback removes SMS setting by key");
    Check(before.SequenceEqual(await Settings(db)), "rollback preserves unrelated ID 20 and all other settings");
    db.Database.Migrate();
    Check(await db.SystemSettings.CountAsync(s => s.Key == smsKey) == 1, "upgrade works again after rollback");
});

if (expectCollision) return;

await Run("fresh installation", async db =>
{
    db.Database.Migrate();
    var sms = await db.SystemSettings.AsNoTracking().SingleAsync(s => s.Key == smsKey);
    Check(sms.IsSensitive && !string.IsNullOrWhiteSpace(sms.Value), "fresh install seeds the sensitive SMS setting");
    Check(!db.Database.HasPendingModelChanges(), "model and migration snapshot agree");
});

foreach (var value in new[] { "admin-customized-key", "" })
{
    await Run("pre-existing SMS key (" + (value.Length == 0 ? "disabled/blank" : "customized") + ")", async db =>
    {
        await db.GetService<IMigrator>().MigrateAsync(previous);
        await AddCliCredentials(db);
        db.SystemSettings.Add(new SystemSetting { Id = 47, Key = smsKey, Value = value, Label = "Admin label", IsSensitive = true });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var before = await Settings(db);
        db.Database.Migrate();
        var sms = await db.SystemSettings.AsNoTracking().SingleAsync(s => s.Key == smsKey);
        Check(sms.Id == 47 && sms.Value == value && sms.Label == "Admin label" && sms.IsSensitive,
            "existing SMS value, ID, label and sensitivity are untouched");
        Check(before.SequenceEqual(await Settings(db)), "existing SMS key does not alter other settings");
    });
}

await Run("installation that already applied the original migration", async db =>
{
    await db.GetService<IMigrator>().MigrateAsync(previous);
    db.SystemSettings.Add(new SystemSetting { Id = 20, Key = smsKey, Value = "already-configured", IsSensitive = true });
    await db.SaveChangesAsync();
    await db.Database.ExecuteSqlRawAsync("INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`) VALUES ({0}, {1})", migration, "9.0.5");
    db.ChangeTracker.Clear();
    db.Database.Migrate();
    var sms = await db.SystemSettings.AsNoTracking().SingleAsync(s => s.Key == smsKey);
    Check(sms.Id == 20 && sms.Value == "already-configured", "already-applied installation remains unchanged");
});
