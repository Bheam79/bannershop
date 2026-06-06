using BannerShop.Api.Services;
using BannerShop.Core.Entities;
using BannerShop.Core.Enums;
using BannerShop.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace BannerShop.Tests;

public class AuthServiceTests
{
    private static (AuthService service, BannerShop.Infrastructure.Data.BannerShopDbContext db, Mock<ITokenService> tokenMock)
        CreateService(string dbName = "")
    {
        var db = DbHelper.CreateInMemory(string.IsNullOrEmpty(dbName) ? null : dbName);

        var tokenMock = new Mock<ITokenService>();
        tokenMock.Setup(t => t.GenerateAccessToken(It.IsAny<User>())).Returns("access-token-stub");
        // Use a factory lambda so each call to GenerateRefreshToken() yields a new unique value
        tokenMock.Setup(t => t.GenerateRefreshToken()).Returns(() => Guid.NewGuid().ToString("N"));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:RefreshTokenExpiryDays"] = "30"
            })
            .Build();

        var service = new AuthService(db, tokenMock.Object, config);
        return (service, db, tokenMock);
    }

    // ── Registration ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_NewEmail_Succeeds_AndReturnsTokens()
    {
        var (service, _, _) = CreateService();

        var result = await service.RegisterAsync("new@example.com", "Password123!", "New User", null);

        result.Success.Should().BeTrue();
        result.AccessToken.Should().Be("access-token-stub");
        result.RefreshToken.Should().NotBeNullOrEmpty();
        result.User.Should().NotBeNull();
        result.User!.Email.Should().Be("new@example.com");
        result.User.Role.Should().Be(UserRole.Customer);
    }

    [Fact]
    public async Task Register_NewEmail_NormalisesEmailToLowercase()
    {
        var (service, db, _) = CreateService();

        await service.RegisterAsync("Upper@Example.COM", "Password123!", "Test", null);

        db.Users.Any(u => u.Email == "upper@example.com").Should().BeTrue();
    }

    [Fact]
    public async Task Register_DuplicateEmail_Fails()
    {
        var (service, _, _) = CreateService();
        await service.RegisterAsync("dup@example.com", "Password123!", "First", null);

        var result = await service.RegisterAsync("DUP@EXAMPLE.COM", "OtherPass!", "Second", null);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("already registered");
    }

    [Fact]
    public async Task Register_HashesPassword_NotStoredInPlainText()
    {
        var (service, db, _) = CreateService();
        const string plainPassword = "MySecret!99";

        await service.RegisterAsync("pw@example.com", plainPassword, "PW Test", null);

        var user = db.Users.First(u => u.Email == "pw@example.com");
        user.PasswordHash.Should().NotBe(plainPassword);
        BCrypt.Net.BCrypt.Verify(plainPassword, user.PasswordHash).Should().BeTrue();
    }

    [Fact]
    public async Task Register_WithPhone_StoresPhoneTrimmed()
    {
        var (service, db, _) = CreateService();

        await service.RegisterAsync("phone@example.com", "P@ss1", "Phone User", "  +47 123 45 678  ");

        var user = db.Users.First(u => u.Email == "phone@example.com");
        user.Phone.Should().Be("+47 123 45 678");
    }

    // ── Login ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_CorrectCredentials_Succeeds()
    {
        var (service, _, _) = CreateService();
        await service.RegisterAsync("login@example.com", "Password123!", "Login User", null);

        var result = await service.LoginAsync("login@example.com", "Password123!");

        result.Success.Should().BeTrue();
        result.AccessToken.Should().Be("access-token-stub");
    }

    [Fact]
    public async Task Login_WrongPassword_Fails()
    {
        var (service, _, _) = CreateService();
        await service.RegisterAsync("lw@example.com", "CorrectPass!", "User", null);

        var result = await service.LoginAsync("lw@example.com", "WrongPass!");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Invalid email or password");
    }

    [Fact]
    public async Task Login_UnknownEmail_Fails()
    {
        var (service, _, _) = CreateService();

        var result = await service.LoginAsync("nobody@example.com", "anyPassword!");

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task Login_EmailMatchesCaseInsensitively()
    {
        var (service, _, _) = CreateService();
        await service.RegisterAsync("case@example.com", "Pass123!", "Case User", null);

        var result = await service.LoginAsync("CASE@EXAMPLE.COM", "Pass123!");

        result.Success.Should().BeTrue();
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_ValidToken_SucceedsAndRotatesToken()
    {
        var (service, _, _) = CreateService();
        var reg = await service.RegisterAsync("ref@example.com", "Pass123!", "Ref User", null);
        var oldRefreshToken = reg.RefreshToken!;

        var result = await service.RefreshAsync(oldRefreshToken);

        result.Success.Should().BeTrue();
        result.RefreshToken.Should().NotBe(oldRefreshToken); // rotated
    }

    [Fact]
    public async Task Refresh_ValidToken_RevokesOldToken()
    {
        var (service, db, _) = CreateService();
        var reg = await service.RegisterAsync("rev@example.com", "Pass123!", "Rev User", null);
        var oldToken = reg.RefreshToken!;

        await service.RefreshAsync(oldToken);

        var stored = db.RefreshTokens.First(t => t.Token == oldToken);
        stored.IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task Refresh_InvalidToken_Fails()
    {
        var (service, _, _) = CreateService();

        var result = await service.RefreshAsync("this-token-does-not-exist");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Invalid or expired");
    }

    [Fact]
    public async Task Refresh_RevokedToken_Fails()
    {
        var (service, _, _) = CreateService();
        var reg = await service.RegisterAsync("rk@example.com", "Pass123!", "RK User", null);
        var token = reg.RefreshToken!;
        await service.RefreshAsync(token); // rotates/revokes it

        var secondRefresh = await service.RefreshAsync(token);

        secondRefresh.Success.Should().BeFalse();
    }

    // ── Logout ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        var (service, db, _) = CreateService();
        var reg = await service.RegisterAsync("logout@example.com", "Pass123!", "Logout User", null);
        var token = reg.RefreshToken!;

        await service.LogoutAsync(token);

        var stored = db.RefreshTokens.First(t => t.Token == token);
        stored.IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task Logout_NonExistentToken_DoesNotThrow()
    {
        var (service, _, _) = CreateService();

        var act = () => service.LogoutAsync("non-existent-token");

        await act.Should().NotThrowAsync();
    }

    // ── Change password ───────────────────────────────────────────────────────

    [Fact]
    public async Task ChangePassword_CorrectCurrentPassword_ReturnsTrue()
    {
        var (service, db, _) = CreateService();
        var reg = await service.RegisterAsync("cpw@example.com", "OldPass123!", "CP User", null);
        var user = db.Users.First(u => u.Email == "cpw@example.com");

        var ok = await service.ChangePasswordAsync(user.Id, "OldPass123!", "NewPass456!");

        ok.Should().BeTrue();
    }

    [Fact]
    public async Task ChangePassword_NewPasswordIsStored()
    {
        var (service, db, _) = CreateService();
        await service.RegisterAsync("cpw2@example.com", "OldPass123!", "CP User", null);
        var user = db.Users.First(u => u.Email == "cpw2@example.com");

        await service.ChangePasswordAsync(user.Id, "OldPass123!", "NewPass456!");

        var updated = db.Users.Find(user.Id)!;
        BCrypt.Net.BCrypt.Verify("NewPass456!", updated.PasswordHash).Should().BeTrue();
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_ReturnsFalse()
    {
        var (service, db, _) = CreateService();
        await service.RegisterAsync("wcp@example.com", "CorrectPass!", "WCP User", null);
        var user = db.Users.First(u => u.Email == "wcp@example.com");

        var ok = await service.ChangePasswordAsync(user.Id, "WrongPass!", "NewPass456!");

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task ChangePassword_UnknownUserId_ReturnsFalse()
    {
        var (service, _, _) = CreateService();

        var ok = await service.ChangePasswordAsync(99999, "AnyPass!", "NewPass!");

        ok.Should().BeFalse();
    }

    // ── GetUser / UpdateProfile ───────────────────────────────────────────────

    [Fact]
    public async Task GetUserAsync_KnownId_ReturnsUser()
    {
        var (service, db, _) = CreateService();
        await service.RegisterAsync("gu@example.com", "Pass123!", "GU User", null);
        var id = db.Users.First(u => u.Email == "gu@example.com").Id;

        var user = await service.GetUserAsync(id);

        user.Should().NotBeNull();
        user!.Email.Should().Be("gu@example.com");
    }

    [Fact]
    public async Task GetUserAsync_UnknownId_ReturnsNull()
    {
        var (service, _, _) = CreateService();

        var user = await service.GetUserAsync(99999);

        user.Should().BeNull();
    }

    [Fact]
    public async Task UpdateProfile_ValidUser_UpdatesNameAndPhone()
    {
        var (service, db, _) = CreateService();
        await service.RegisterAsync("up@example.com", "Pass123!", "Old Name", null);
        var id = db.Users.First(u => u.Email == "up@example.com").Id;

        var updated = await service.UpdateProfileAsync(id, "New Name", "+4799999999");

        updated.Should().NotBeNull();
        updated!.Name.Should().Be("New Name");
        updated.Phone.Should().Be("+4799999999");
    }
}
