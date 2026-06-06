using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BannerShop.Api.Services;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BannerShop.Tests;

public class TokenServiceTests
{
    private const string Secret   = "test-secret-at-least-32-chars-longXYZ!";
    private const string Issuer   = "bannershop.test";
    private const string Audience = "bannershop.test";

    private static TokenService CreateService(int expiryMinutes = 60)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"]                   = Secret,
                ["Jwt:Issuer"]                   = Issuer,
                ["Jwt:Audience"]                 = Audience,
                ["Jwt:AccessTokenExpiryMinutes"] = expiryMinutes.ToString()
            })
            .Build();

        return new TokenService(config);
    }

    // ── GenerateAccessToken ──────────────────────────────────────────────────

    [Fact]
    public void GenerateAccessToken_ContainsNameIdentifierClaim()
    {
        var service = CreateService();
        var user = new User { Id = 42, Email = "test@example.com", Name = "Alice", Role = UserRole.Customer };

        var jwt = service.GenerateAccessToken(user);

        var handler = new JwtSecurityTokenHandler();
        var parsed = handler.ReadJwtToken(jwt);
        parsed.Claims
            .Should().Contain(c => c.Type == ClaimTypes.NameIdentifier && c.Value == "42");
    }

    [Fact]
    public void GenerateAccessToken_ContainsEmailClaim()
    {
        var service = CreateService();
        var user = new User { Id = 1, Email = "alice@example.com", Name = "Alice", Role = UserRole.Customer };

        var jwt = service.GenerateAccessToken(user);

        var handler = new JwtSecurityTokenHandler();
        var parsed = handler.ReadJwtToken(jwt);
        parsed.Claims
            .Should().Contain(c => c.Type == ClaimTypes.Email && c.Value == "alice@example.com");
    }

    [Fact]
    public void GenerateAccessToken_ContainsNameClaim()
    {
        var service = CreateService();
        var user = new User { Id = 1, Email = "alice@example.com", Name = "Alice Smith", Role = UserRole.Customer };

        var jwt = service.GenerateAccessToken(user);

        var handler = new JwtSecurityTokenHandler();
        var parsed = handler.ReadJwtToken(jwt);
        parsed.Claims
            .Should().Contain(c => c.Type == ClaimTypes.Name && c.Value == "Alice Smith");
    }

    [Fact]
    public void GenerateAccessToken_ContainsRoleClaim()
    {
        var service = CreateService();
        var user = new User { Id = 1, Email = "admin@example.com", Name = "Admin", Role = UserRole.Admin };

        var jwt = service.GenerateAccessToken(user);

        var handler = new JwtSecurityTokenHandler();
        var parsed = handler.ReadJwtToken(jwt);
        parsed.Claims
            .Should().Contain(c => c.Type == ClaimTypes.Role && c.Value == "Admin");
    }

    [Fact]
    public void GenerateAccessToken_CustomerRoleClaim_IsCustomer()
    {
        var service = CreateService();
        var user = new User { Id = 1, Email = "user@example.com", Name = "User", Role = UserRole.Customer };

        var jwt = service.GenerateAccessToken(user);

        var handler = new JwtSecurityTokenHandler();
        var parsed = handler.ReadJwtToken(jwt);
        parsed.Claims
            .Should().Contain(c => c.Type == ClaimTypes.Role && c.Value == "Customer");
    }

    [Fact]
    public void GenerateAccessToken_HasCorrectIssuerAndAudience()
    {
        var service = CreateService();
        var user = new User { Id = 1, Email = "u@x.com", Name = "U", Role = UserRole.Customer };

        var jwt = service.GenerateAccessToken(user);

        var handler = new JwtSecurityTokenHandler();
        var parsed = handler.ReadJwtToken(jwt);
        parsed.Issuer.Should().Be(Issuer);
        parsed.Audiences.Should().Contain(Audience);
    }

    [Fact]
    public void GenerateAccessToken_ExpiryMatchesConfiguration()
    {
        var service = CreateService(expiryMinutes: 30);
        var user = new User { Id = 1, Email = "u@x.com", Name = "U", Role = UserRole.Customer };

        var before = DateTime.UtcNow.AddMinutes(29);
        var jwt = service.GenerateAccessToken(user);
        var after  = DateTime.UtcNow.AddMinutes(31);

        var handler = new JwtSecurityTokenHandler();
        var parsed = handler.ReadJwtToken(jwt);
        parsed.ValidTo.Should().BeAfter(before).And.BeBefore(after);
    }

    [Fact]
    public void GenerateAccessToken_IsSignedWithHmacSha256()
    {
        var service = CreateService();
        var user = new User { Id = 1, Email = "u@x.com", Name = "U", Role = UserRole.Customer };

        var jwt = service.GenerateAccessToken(user);

        var handler = new JwtSecurityTokenHandler();
        var parsed = handler.ReadJwtToken(jwt);
        parsed.Header.Alg.Should().Be("HS256");
    }

    // ── GenerateRefreshToken ─────────────────────────────────────────────────

    [Fact]
    public void GenerateRefreshToken_ReturnsNonEmptyBase64String()
    {
        var service = CreateService();

        var token = service.GenerateRefreshToken();

        token.Should().NotBeNullOrWhiteSpace();
        var act = () => Convert.FromBase64String(token);
        act.Should().NotThrow();
    }

    [Fact]
    public void GenerateRefreshToken_Returns88CharBase64_For64Bytes()
    {
        var service = CreateService();

        var token = service.GenerateRefreshToken();

        // 64 bytes base64-encoded = 88 chars (with padding)
        var bytes = Convert.FromBase64String(token);
        bytes.Should().HaveCount(64);
    }

    [Fact]
    public void GenerateRefreshToken_EachCallProducesUniqueToken()
    {
        var service = CreateService();

        var t1 = service.GenerateRefreshToken();
        var t2 = service.GenerateRefreshToken();

        t1.Should().NotBe(t2);
    }
}
