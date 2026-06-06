using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BannerShop.Tests.Helpers;

/// <summary>
/// Custom WebApplicationFactory that replaces the MySQL DbContext with an InMemory database
/// and provides test configuration overrides.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string JwtSecret  = "test-secret-at-least-32-chars-longXYZ!";
    public const string JwtIssuer  = "bannershop.no";
    public const string JwtAudience = "bannershop.no";

    private readonly string _dbName = "TestDb_" + Guid.NewGuid().ToString("N");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Use a non-Development environment so Program.cs does NOT call db.Database.Migrate()
        builder.UseEnvironment("Testing");

        // Provide test-safe configuration values
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=bannershoptest;User=test;Password=test;",
                ["Jwt:Secret"]                          = JwtSecret,
                ["Jwt:Issuer"]                          = JwtIssuer,
                ["Jwt:Audience"]                        = JwtAudience,
                ["Jwt:AccessTokenExpiryMinutes"]        = "60",
                ["Jwt:RefreshTokenExpiryDays"]          = "30",
                // No Admin:SeedPassword → SeedAdminAsync returns early
            });
        });

        builder.ConfigureServices(services =>
        {
            // ── Remove ALL DbContext registrations (options + scoped context itself) ──
            // This is necessary because Pomelo registers provider-specific EF Core services
            // that conflict with the InMemory provider if both are present in the same
            // IServiceProvider's internal EF Core service cache.
            var toRemove = services
                .Where(d =>
                    d.ServiceType == typeof(DbContextOptions<BannerShopDbContext>) ||
                    d.ServiceType == typeof(BannerShopDbContext))
                .ToList();
            foreach (var d in toRemove)
                services.Remove(d);

            // ── Build a dedicated service provider that ONLY knows about InMemory ──
            // Using UseInternalServiceProvider avoids EF Core's "two providers registered"
            // check which would fire if we relied on the shared application service provider
            // that still has Pomelo services registered from Program.cs.
            var inMemoryProvider = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();

            services.AddDbContext<BannerShopDbContext>(options =>
                options.UseInMemoryDatabase(_dbName)
                       .UseInternalServiceProvider(inMemoryProvider));

            // ── Override JWT signing/validation key ────────────────────────────────
            // Program.cs may capture the JWT secret before our ConfigureAppConfiguration
            // override takes effect. PostConfigure ensures validators and token generators
            // both use the same test secret regardless of initialization order.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, opts =>
            {
                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret));
                opts.TokenValidationParameters.IssuerSigningKey = key;
                opts.TokenValidationParameters.ValidIssuer   = JwtIssuer;
                opts.TokenValidationParameters.ValidAudience = JwtAudience;
            });
        });
    }

    // ── Helpers for seeding and authentication ───────────────────────────────

    /// <summary>Runs a seeder action against the test database within a scoped service context.</summary>
    public void SeedDatabase(Action<BannerShopDbContext> seeder)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BannerShopDbContext>();
        seeder(db);
    }

    /// <summary>
    /// Generates a signed JWT for a test user — bypasses the auth endpoint so tests
    /// can authenticate without seeding the DB first.
    /// </summary>
    public static string MakeJwt(int userId = 1, string email = "test@example.com",
                                  string name = "Test User", UserRole role = UserRole.Customer)
    {
        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Name, name),
            new Claim(ClaimTypes.Role, role.ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Creates an HttpClient with an Authorization: Bearer header pre-set.</summary>
    public HttpClient CreateAuthenticatedClient(int userId = 1, string email = "test@example.com",
                                                 string name = "Test User", UserRole role = UserRole.Customer)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", MakeJwt(userId, email, name, role));
        return client;
    }
}

/// <summary>Extension helpers for reading JSON from HttpResponseMessage.</summary>
internal static class HttpResponseExtensions
{
    private static readonly JsonSerializerOptions _opts =
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

    public static async Task<T?> ReadJsonAsync<T>(this HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(json, _opts);
    }
}
