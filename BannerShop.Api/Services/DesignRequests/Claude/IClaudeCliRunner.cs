namespace BannerShop.Api.Services.DesignRequests.Claude;

public interface IClaudeCliRunner
{
    Task<string> RunAsync(
        string systemPrompt,
        string userPrompt,
        string oauthToken,
        CancellationToken ct);
}
