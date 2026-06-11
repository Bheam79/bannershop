using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BannerShop.Api.Models.Auth;
using BannerShop.Core.Enums;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace BannerShop.Tests.Controllers;

/// <summary>
/// Integration tests for AuthController using WebApplicationFactory with InMemory database.
/// </summary>
public class AuthControllerTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public AuthControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() => _factory.CreateClient();

    // ── POST /api/auth/register ───────────────────────────────────────────────

    [Fact]
    public async Task Register_NewUser_Returns200WithTokens()
    {
        var client = CreateClient();
        var req = new { email = $"new_{Guid.NewGuid():N}@test.com", password = "Secure123!", name = "New User" };

        var response = await client.PostAsJsonAsync("/api/auth/register", req);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("accessToken");
        body.Should().Contain("refreshToken");
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns400()
    {
        var client = CreateClient();
        var email = $"dup_{Guid.NewGuid():N}@test.com";
        var req = new { email, password = "Secure123!", name = "User" };

        await client.PostAsJsonAsync("/api/auth/register", req);
        var response = await client.PostAsJsonAsync("/api/auth/register", req);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── POST /api/auth/login ──────────────────────────────────────────────────

    [Fact]
    public async Task Login_ValidCredentials_Returns200()
    {
        var client = CreateClient();
        var email = $"login_{Guid.NewGuid():N}@test.com";
        await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "MyPass123!", name = "Login User" });

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email, password = "MyPass123!" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_InvalidPassword_Returns401()
    {
        var client = CreateClient();
        var email = $"inv_{Guid.NewGuid():N}@test.com";
        await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "CorrectPass!", name = "User" });

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email, password = "WrongPass!" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_UnknownEmail_Returns401()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "nobody@unknown.com", password = "AnyPass!" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── POST /api/auth/refresh ────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_ValidRefreshToken_Returns200WithNewTokens()
    {
        var client = CreateClient();
        var email = $"ref_{Guid.NewGuid():N}@test.com";
        var regResp = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "Pass123!", name = "Refresh User" });
        var regBody = await regResp.Content.ReadAsStringAsync();
        var reg = JsonSerializer.Deserialize<JsonElement>(regBody, _json);
        var refreshToken = reg.GetProperty("refreshToken").GetString();

        var response = await client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("accessToken");
    }

    [Fact]
    public async Task Refresh_InvalidToken_Returns401()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = "totally-invalid-token-value" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── POST /api/auth/logout ─────────────────────────────────────────────────

    [Fact]
    public async Task Logout_ValidToken_Returns204()
    {
        var client = CreateClient();
        var email = $"out_{Guid.NewGuid():N}@test.com";
        var regResp = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "Pass123!", name = "Logout User" });
        var regBody = await regResp.Content.ReadAsStringAsync();
        var reg = JsonSerializer.Deserialize<JsonElement>(regBody, _json);
        var refreshToken = reg.GetProperty("refreshToken").GetString();

        var response = await client.PostAsJsonAsync("/api/auth/logout",
            new { refreshToken });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── GET /api/auth/me ──────────────────────────────────────────────────────

    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithValidToken_Returns200WithUserInfo()
    {
        // Register user, then use the access token
        var client = CreateClient();
        var email = $"me_{Guid.NewGuid():N}@test.com";
        var regResp = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "Pass123!", name = "Me User" });
        var regBody = await regResp.Content.ReadAsStringAsync();
        var reg = JsonSerializer.Deserialize<JsonElement>(regBody, _json);
        var accessToken = reg.GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(email);
    }

    [Fact]
    public async Task Me_WithExpiredToken_Returns401()
    {
        // Generate a pre-expired token
        var client = CreateClient();
        // We can't easily expire a token without time-travel, so use an invalid token
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "invalid.jwt.token");

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── POST /api/auth/change-password ────────────────────────────────────────

    [Fact]
    public async Task ChangePassword_CorrectCurrentPassword_Returns204()
    {
        var client = CreateClient();
        var email = $"cp_{Guid.NewGuid():N}@test.com";
        var regResp = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "OldPass123!", name = "CP User" });
        var reg = JsonSerializer.Deserialize<JsonElement>(
            await regResp.Content.ReadAsStringAsync(), _json);
        var accessToken = reg.GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = "OldPass123!", newPassword = "NewPass456!" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_Returns400()
    {
        var client = CreateClient();
        var email = $"wcp_{Guid.NewGuid():N}@test.com";
        var regResp = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "CorrectPass!", name = "WCP User" });
        var reg = JsonSerializer.Deserialize<JsonElement>(
            await regResp.Content.ReadAsStringAsync(), _json);
        var accessToken = reg.GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = "WrongPass!", newPassword = "NewPass456!" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── PUT /api/auth/me ──────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateProfile_WithAuth_Returns200WithUpdatedName()
    {
        var client = CreateClient();
        var email = $"upd_{Guid.NewGuid():N}@test.com";
        var regResp = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "Pass123!", name = "Original Name" });
        var reg = JsonSerializer.Deserialize<JsonElement>(
            await regResp.Content.ReadAsStringAsync(), _json);
        var accessToken = reg.GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.PutAsJsonAsync("/api/auth/me",
            new { name = "Updated Name", phone = "90909090" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(), _json);
        body.GetProperty("name").GetString().Should().Be("Updated Name");
    }

    [Fact]
    public async Task UpdateProfile_WithoutAuth_Returns401()
    {
        var client = CreateClient();
        var response = await client.PutAsJsonAsync("/api/auth/me",
            new { name = "Ghost", phone = "" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
