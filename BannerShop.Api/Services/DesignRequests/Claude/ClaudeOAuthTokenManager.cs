using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using BannerShop.Api.Services.SystemSettings;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace BannerShop.Api.Services.DesignRequests.Claude;

public sealed record ClaudeOAuthStartResult(string AuthorizationUrl, DateTimeOffset ExpiresAt);

public sealed record ClaudeOAuthStatus(
    bool IsConfigured,
    bool CanRefresh,
    string Source,
    DateTimeOffset? ExpiresAt);

public interface IClaudeOAuthTokenProvider
{
    Task<string?> GetAccessTokenAsync(CancellationToken ct = default);
}

/// <summary>
/// Owns the admin-initiated Claude Code PKCE flow and refresh-token rotation.
/// OAuth nonces live in memory for ten minutes; credentials are stored only in
/// sensitive system settings and are never returned by the admin API.
/// </summary>
public sealed class ClaudeOAuthTokenManager : IClaudeOAuthTokenProvider
{
    public const string AccessTokenSetting = "claude_code_oauth_token";
    public const string RefreshTokenSetting = "claude_code_oauth_refresh_token";
    public const string ExpiresAtSetting = "claude_code_oauth_expires_at";

    private static readonly TimeSpan FlowLifetime = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, PendingFlow> _pendingFlows = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<ClaudeCliOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ClaudeOAuthTokenManager> _log;

    public int RefreshIntervalMinutes => _options.CurrentValue.OAuthRefreshIntervalMinutes;

    public ClaudeOAuthTokenManager(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<ClaudeCliOptions> options,
        TimeProvider timeProvider,
        ILogger<ClaudeOAuthTokenManager> log)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _options = options;
        _timeProvider = timeProvider;
        _log = log;
    }

    public ClaudeOAuthStartResult StartAuthorization()
    {
        RemoveExpiredFlows();

        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(verifier)));
        var expiresAt = _timeProvider.GetUtcNow().Add(FlowLifetime);
        _pendingFlows[state] = new PendingFlow(verifier, expiresAt);

        var options = _options.CurrentValue;
        var query = new Dictionary<string, string?>
        {
            ["code"] = "true",
            ["client_id"] = options.OAuthClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = options.OAuthRedirectUrl,
            ["scope"] = options.OAuthScope,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state
        };

        return new ClaudeOAuthStartResult(
            QueryHelpers.AddQueryString(options.OAuthAuthorizeUrl, query),
            expiresAt);
    }

    public async Task CompleteAuthorizationAsync(string pastedCode, CancellationToken ct = default)
    {
        var (code, state) = ParseAuthorizationCode(pastedCode);
        if (!_pendingFlows.TryRemove(state, out var flow))
            throw new ClaudeOAuthInputException("OAuth-forespørselen finnes ikke eller er allerede brukt. Start tilkoblingen på nytt.");
        if (flow.ExpiresAt <= _timeProvider.GetUtcNow())
            throw new ClaudeOAuthInputException("OAuth-forespørselen er utløpt. Start tilkoblingen på nytt.");

        var options = _options.CurrentValue;
        var tokens = await RequestTokensAsync(new
        {
            grant_type = "authorization_code",
            code,
            redirect_uri = options.OAuthRedirectUrl,
            client_id = options.OAuthClientId,
            code_verifier = flow.Verifier,
            state
        }, ct);

        if (string.IsNullOrWhiteSpace(tokens.RefreshToken))
            throw new ClaudeOAuthProtocolException("Claude returnerte ikke et refresh-token.");

        await StoreTokensAsync(tokens, tokens.RefreshToken, ct);
        _log.LogInformation("Claude OAuth authorization completed; refreshable credentials stored.");
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
    {
        await RefreshIfNeededAsync(ct);
        var stored = await GetSettingAsync(AccessTokenSetting, ct);
        return stored ?? Environment.GetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN");
    }

    public async Task<ClaudeOAuthStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var access = await GetSettingAsync(AccessTokenSetting, ct);
        var refresh = await GetSettingAsync(RefreshTokenSetting, ct);
        var expiresAt = ParseExpiry(await GetSettingAsync(ExpiresAtSetting, ct));
        var environment = Environment.GetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN");

        return new ClaudeOAuthStatus(
            !string.IsNullOrWhiteSpace(access) || !string.IsNullOrWhiteSpace(environment),
            !string.IsNullOrWhiteSpace(refresh) && expiresAt.HasValue,
            !string.IsNullOrWhiteSpace(access) ? "database" :
                !string.IsNullOrWhiteSpace(environment) ? "environment" : "none",
            expiresAt);
    }

    /// <summary>Refreshes a short-lived OAuth credential close to expiry.</summary>
    public async Task<bool> RefreshIfNeededAsync(CancellationToken ct = default)
    {
        var refreshToken = await GetSettingAsync(RefreshTokenSetting, ct);
        var expiresAt = ParseExpiry(await GetSettingAsync(ExpiresAtSetting, ct));
        if (string.IsNullOrWhiteSpace(refreshToken) || !expiresAt.HasValue || !NeedsRefresh(expiresAt.Value))
            return false;

        await _refreshLock.WaitAsync(ct);
        try
        {
            // A request or the background loop may have refreshed while waiting.
            refreshToken = await GetSettingAsync(RefreshTokenSetting, ct);
            expiresAt = ParseExpiry(await GetSettingAsync(ExpiresAtSetting, ct));
            if (string.IsNullOrWhiteSpace(refreshToken) || !expiresAt.HasValue || !NeedsRefresh(expiresAt.Value))
                return false;

            var options = _options.CurrentValue;
            var tokens = await RequestTokensAsync(new
            {
                grant_type = "refresh_token",
                refresh_token = refreshToken,
                client_id = options.OAuthClientId,
                scope = options.OAuthScope
            }, ct);

            await StoreTokensAsync(tokens, tokens.RefreshToken ?? refreshToken, ct);
            _log.LogInformation("Claude OAuth access token refreshed; next expiry is {ExpiresAt}.",
                _timeProvider.GetUtcNow().AddSeconds(tokens.ExpiresIn));
            return true;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private bool NeedsRefresh(DateTimeOffset expiresAt)
    {
        var minutes = Math.Max(1, _options.CurrentValue.OAuthRefreshBeforeExpiryMinutes);
        return expiresAt <= _timeProvider.GetUtcNow().AddMinutes(minutes);
    }

    private async Task<TokenResponse> RequestTokensAsync(object request, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("ClaudeOAuth");
        using var response = await client.PostAsJsonAsync(_options.CurrentValue.OAuthTokenUrl, request, ct);
        if (!response.IsSuccessStatusCode)
        {
            _log.LogWarning("Claude OAuth token endpoint returned HTTP {StatusCode}.", (int)response.StatusCode);
            throw new ClaudeOAuthProtocolException(
                $"Claude OAuth svarte med HTTP {(int)response.StatusCode}. Prøv igjen eller start tilkoblingen på nytt.");
        }

        TokenResponse? tokens;
        try
        {
            tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct);
        }
        catch (JsonException ex)
        {
            throw new ClaudeOAuthProtocolException("Claude returnerte et ugyldig token-svar.", ex);
        }

        if (tokens is null || string.IsNullOrWhiteSpace(tokens.AccessToken) || tokens.ExpiresIn <= 0)
            throw new ClaudeOAuthProtocolException("Claude returnerte et ufullstendig token-svar.");
        return tokens;
    }

    private async Task StoreTokensAsync(TokenResponse tokens, string refreshToken, CancellationToken ct)
    {
        var expiresAt = _timeProvider.GetUtcNow().AddSeconds(tokens.ExpiresIn);
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISystemSettingsService>();
        await settings.SetValuesAsync(new Dictionary<string, string>
        {
            [AccessTokenSetting] = tokens.AccessToken,
            [RefreshTokenSetting] = refreshToken,
            [ExpiresAtSetting] = expiresAt.ToString("O")
        }, ct);
    }

    private async Task<string?> GetSettingAsync(string key, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISystemSettingsService>()
            .GetValueAsync(key, ct);
    }

    private static (string Code, string State) ParseAuthorizationCode(string value)
    {
        var parts = value.Trim().Split('#', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            throw new ClaudeOAuthInputException("Lim inn hele koden fra Claude i formatet kode#state.");
        return (parts[0], parts[1]);
    }

    private static DateTimeOffset? ParseExpiry(string? value) =>
        DateTimeOffset.TryParse(value, out var result) ? result : null;

    private void RemoveExpiredFlows()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var (state, flow) in _pendingFlows)
            if (flow.ExpiresAt <= now)
                _pendingFlows.TryRemove(state, out _);
    }

    private sealed record PendingFlow(string Verifier, DateTimeOffset ExpiresAt);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

public sealed class ClaudeOAuthInputException(string message) : Exception(message);

public sealed class ClaudeOAuthProtocolException : Exception
{
    public ClaudeOAuthProtocolException(string message) : base(message) { }
    public ClaudeOAuthProtocolException(string message, Exception innerException) : base(message, innerException) { }
}
