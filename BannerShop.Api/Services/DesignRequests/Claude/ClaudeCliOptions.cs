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
}
