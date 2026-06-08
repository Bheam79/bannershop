namespace BannerShop.Api.Services.DesignRequests;

/// <summary>
/// Pass-through prompt refiner used when no LLM provider is configured
/// (dev / test without an OpenAI key). Returns the deterministic base prompt
/// unchanged so the pipeline behaves identically to pre-BANNERSH-61.
/// </summary>
public sealed class NoopPromptRefinementService : IPromptRefinementService
{
    public Task<string> RefineAsync(PromptRefinementInput input, CancellationToken ct)
        => Task.FromResult(input.BasePrompt);
}
