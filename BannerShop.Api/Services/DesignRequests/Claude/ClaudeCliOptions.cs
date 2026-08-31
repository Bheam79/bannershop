namespace BannerShop.Api.Services.DesignRequests.Claude;

/// <summary>
/// Non-secret process settings for Claude Code prompt refinement. Authentication
/// is read from <c>system_settings.claude_code_oauth_token</c> (or the
/// <c>CLAUDE_CODE_OAUTH_TOKEN</c> environment variable when the DB value is
/// blank) for every invocation.
/// </summary>
public sealed class ClaudeCliOptions
{
    public const string SectionName = "ClaudeCli";

    public string ExecutablePath { get; set; } = "claude";
    public string Model { get; set; } = "sonnet";
    public int TimeoutSeconds { get; set; } = 60;

    // Non-secret Claude Code OAuth client metadata. Keep these configurable so
    // an Anthropic endpoint/client change does not require a code change.
    public string OAuthAuthorizeUrl { get; set; } = "https://claude.com/cai/oauth/authorize";
    public string OAuthTokenUrl { get; set; } = "https://platform.claude.com/v1/oauth/token";
    public string OAuthClientId { get; set; } = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
    public string OAuthRedirectUrl { get; set; } = "https://platform.claude.com/oauth/code/callback";
    public string OAuthScope { get; set; } = "user:inference";
    public int OAuthRefreshIntervalMinutes { get; set; } = 5;
    public int OAuthRefreshBeforeExpiryMinutes { get; set; } = 10;
}
