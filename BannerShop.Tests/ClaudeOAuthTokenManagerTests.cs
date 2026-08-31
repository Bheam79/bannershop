using System.Net;
using System.Text;
using BannerShop.Api.Services.DesignRequests.Claude;
using BannerShop.Api.Services.SystemSettings;
using BannerShop.Core.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BannerShop.Tests;

public sealed class ClaudeOAuthTokenManagerTests
{
    [Fact]
    public async Task Pkce_flow_exchanges_code_and_stores_refreshable_credentials()
    {
        string? requestBody = null;
        var handler = new StubHandler(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            return Json("""
                {"access_token":"access-new","refresh_token":"refresh-new","expires_in":3600}
                """);
        });
        var settings = new MemorySettings();
        var now = new DateTimeOffset(2026, 8, 31, 10, 0, 0, TimeSpan.Zero);
        var manager = CreateManager(settings, handler, now);

        var start = manager.StartAuthorization();
        var uri = new Uri(start.AuthorizationUrl);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        query["scope"].ToString().Should().Be("user:inference");
        query["code_challenge_method"].ToString().Should().Be("S256");
        query["code_challenge"].ToString().Should().NotBeNullOrWhiteSpace();

        await manager.CompleteAuthorizationAsync($"authorization-code#{query["state"]}");

        requestBody.Should().Contain("\"grant_type\":\"authorization_code\"");
        requestBody.Should().Contain("\"code_verifier\"");
        settings.Values[ClaudeOAuthTokenManager.AccessTokenSetting].Should().Be("access-new");
        settings.Values[ClaudeOAuthTokenManager.RefreshTokenSetting].Should().Be("refresh-new");
        (await manager.GetStatusAsync()).CanRefresh.Should().BeTrue();
    }

    [Fact]
    public async Task Expiring_token_is_refreshed_and_rotated_before_use()
    {
        string? requestBody = null;
        var handler = new StubHandler(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            return Json("""
                {"access_token":"access-rotated","refresh_token":"refresh-rotated","expires_in":3600}
                """);
        });
        var now = new DateTimeOffset(2026, 8, 31, 10, 0, 0, TimeSpan.Zero);
        var settings = new MemorySettings(new Dictionary<string, string>
        {
            [ClaudeOAuthTokenManager.AccessTokenSetting] = "access-old",
            [ClaudeOAuthTokenManager.RefreshTokenSetting] = "refresh-old",
            [ClaudeOAuthTokenManager.ExpiresAtSetting] = now.AddMinutes(2).ToString("O")
        });
        var manager = CreateManager(settings, handler, now);

        var token = await manager.GetAccessTokenAsync();

        token.Should().Be("access-rotated");
        requestBody.Should().Contain("\"grant_type\":\"refresh_token\"");
        requestBody.Should().Contain("\"refresh_token\":\"refresh-old\"");
        settings.Values[ClaudeOAuthTokenManager.RefreshTokenSetting].Should().Be("refresh-rotated");
    }

    [Fact]
    public async Task Completion_rejects_code_with_wrong_state_without_calling_token_endpoint()
    {
        var calls = 0;
        var manager = CreateManager(
            new MemorySettings(),
            new StubHandler(_ =>
            {
                calls++;
                return Task.FromResult(Json("{}"));
            }),
            DateTimeOffset.UtcNow);
        manager.StartAuthorization();

        var action = () => manager.CompleteAuthorizationAsync("code#wrong-state");

        await action.Should().ThrowAsync<ClaudeOAuthInputException>();
        calls.Should().Be(0);
    }

    private static ClaudeOAuthTokenManager CreateManager(
        MemorySettings settings,
        HttpMessageHandler handler,
        DateTimeOffset now)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISystemSettingsService>(settings);
        var provider = services.BuildServiceProvider();
        var options = new OptionsMonitorStub<ClaudeCliOptions>(new ClaudeCliOptions());
        return new ClaudeOAuthTokenManager(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new HttpClientFactoryStub(handler),
            options,
            new FixedTimeProvider(now),
            NullLogger<ClaudeOAuthTokenManager>.Instance);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class MemorySettings : ISystemSettingsService
    {
        public Dictionary<string, string> Values { get; }

        public MemorySettings(Dictionary<string, string>? values = null) =>
            Values = values ?? new Dictionary<string, string>();

        public Task<string?> GetValueAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(Values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : null);

        public Task SetValueAsync(string key, string value, CancellationToken ct = default)
        {
            Values[key] = value;
            return Task.CompletedTask;
        }

        public Task SetValuesAsync(IReadOnlyDictionary<string, string> values, CancellationToken ct = default)
        {
            foreach (var (key, value) in values)
                Values[key] = value;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SystemSettingDto>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SystemSettingDto>>([]);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            callback(request);
    }

    private sealed class HttpClientFactoryStub(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class OptionsMonitorStub<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
