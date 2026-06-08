using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BannerShop.Core.Enums;
using Microsoft.Extensions.Options;

namespace BannerShop.Api.Services.DesignRequests.OpenAi;

/// <summary>
/// LLM-backed prompt rewriter that asks an OpenAI chat model (default
/// <c>gpt-4o-mini</c>) to turn the customer's terse input into a richer
/// image-edit prompt suitable for <c>gpt-image-2</c>'s <c>/v1/images/edits</c>
/// endpoint.
///
/// Worked example from BANNERSH-61: a customer types just "minecraft" as the
/// theme on a birthday banner. The refiner is told the category is Birthday,
/// the celebrant's name + age, the overlay text, and that a portrait was
/// uploaded — and returns a prompt like
/// <c>"Create a Minecraft-inspired birthday banner with @image1 the photo of
/// the celebrant embedded. Match the color and lighting of the photo somewhat
/// …"</c>.
///
/// Implementation notes:
///  - Always-on safety net: any HTTP / JSON / model error returns the
///    deterministic base prompt. This service must NEVER block image
///    generation just because the LLM was slow or rate-limited.
///  - Uses its own per-call <see cref="CancellationTokenSource"/> bounded by
///    <see cref="OpenAiOptions.ChatTimeoutSeconds"/> so a slow refinement
///    doesn't burn the larger image-generation timeout budget.
/// </summary>
public sealed class OpenAiPromptRefinementService : IPromptRefinementService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly IOptionsMonitor<OpenAiOptions> _optsMonitor;
    private readonly ILogger<OpenAiPromptRefinementService> _log;

    public OpenAiPromptRefinementService(
        HttpClient http,
        IOptionsMonitor<OpenAiOptions> optsMonitor,
        ILogger<OpenAiPromptRefinementService> log)
    {
        _http = http;
        _optsMonitor = optsMonitor;
        _log = log;

        var opts = optsMonitor.CurrentValue;
        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri(opts.BaseUrl);
        // Authorization header is set per-request (see RefineAsync) so that a key
        // added to appsettings.Local.json after startup is picked up immediately.
        if (!string.IsNullOrWhiteSpace(opts.OrgId))
            _http.DefaultRequestHeaders.TryAddWithoutValidation("OpenAI-Organization", opts.OrgId);
        // HttpClient.Timeout is global — keep it generous; per-call cancellation
        // is done via the linked CancellationTokenSource below.
        _http.Timeout = TimeSpan.FromSeconds(Math.Max(opts.ChatTimeoutSeconds * 2, 60));
    }

    public async Task<string> RefineAsync(PromptRefinementInput input, CancellationToken ct)
    {
        var opts = _optsMonitor.CurrentValue;

        // Set Authorization per-call so a key updated in appsettings.Local.json
        // takes effect without a service restart.
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", opts.ApiKey);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, opts.ChatTimeoutSeconds)));

        try
        {
            var (systemMsg, userMsg) = BuildMessages(input);
            var payload = new
            {
                model = opts.ChatModel,
                messages = new object[]
                {
                    new { role = "system", content = systemMsg },
                    new { role = "user",   content = userMsg   }
                },
                // A small amount of randomness is fine — we just don't want
                // wildly different prompts on retries.
                temperature = 0.6,
                // Caps the output. The refined prompt is rarely more than a
                // few hundred tokens; we keep a comfortable ceiling.
                max_tokens = 600
            };

            using var resp = await _http.PostAsJsonAsync("/v1/chat/completions", payload, Json, timeoutCts.Token);
            var body = await resp.Content.ReadAsStringAsync(timeoutCts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning(
                    "OpenAI chat-completions returned {Status} for prompt refinement; using base prompt. Body: {Body}",
                    (int)resp.StatusCode, Trunc(body, 800));
                return input.BasePrompt;
            }

            var parsed = JsonSerializer.Deserialize<ChatResponse>(body, Json);
            var refined = parsed?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
            if (string.IsNullOrWhiteSpace(refined))
            {
                _log.LogWarning("OpenAI chat-completions returned an empty refined prompt; using base prompt.");
                return input.BasePrompt;
            }

            // Strip any surrounding code fences the model sometimes adds.
            refined = StripFences(refined);

            _log.LogDebug("Prompt refined: base={BaseLen} chars -> refined={RefinedLen} chars",
                input.BasePrompt.Length, refined.Length);
            return refined;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning("Prompt refinement timed out after {Seconds}s; using base prompt.",
                opts.ChatTimeoutSeconds);
            return input.BasePrompt;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Prompt refinement failed; using base prompt.");
            return input.BasePrompt;
        }
    }

    // ── Prompt construction ──────────────────────────────────────────────────

    private static (string System, string User) BuildMessages(PromptRefinementInput input)
    {
        var system = new StringBuilder();
        system.AppendLine("You are a prompt engineer for an AI image generator that produces large-format printed party banners.");
        system.AppendLine("The image model is OpenAI's gpt-image-2, called through the /v1/images/edits endpoint when a portrait of the celebrant is supplied.");
        system.AppendLine();
        system.AppendLine("Goals when rewriting the customer's request into the final image prompt:");
        system.AppendLine("  • Stay faithful to the celebration category and any theme keywords the customer gave.");
        system.AppendLine("  • Expand terse themes (e.g. \"minecraft\", \"tropical\", \"pirates\") into a vivid, concrete visual description.");
        system.AppendLine("  • If a portrait was uploaded, instruct the model to embed the celebrant's photo prominently and preserve their face, matching the colour and lighting of the photo to the surrounding scene.");
        system.AppendLine("  • Keep the customer's exact overlay text in double-quotes, in the requested language, with the instruction to render it as large, readable banner typography.");
        system.AppendLine("  • Specify the aspect ratio, photorealistic / print-quality, no watermarks, no logos.");
        system.AppendLine();
        system.AppendLine("Output rules (important):");
        system.AppendLine("  • Reply with the refined image-generation prompt ONLY. No preamble, no markdown, no code fences.");
        system.AppendLine("  • One paragraph. English. ≤ 400 words.");

        var user = new StringBuilder();
        user.Append("Celebration category: ").AppendLine(input.Category.ToString());
        user.Append("Person-centred celebration: ").AppendLine(input.Category.IsPersonCentred() ? "yes" : "no");
        user.Append("Celebrant name: ").AppendLine(string.IsNullOrWhiteSpace(input.PersonName) ? "(not given)" : input.PersonName);
        user.Append("Celebrant age: ").AppendLine(input.PersonAge.HasValue ? input.PersonAge.Value.ToString() : "(not given)");
        user.Append("Banner overlay text (must be rendered verbatim): \"").Append(input.TextContent?.Replace("\"", "\\\"")).AppendLine("\"");
        user.Append("Overlay text language: ").AppendLine(string.Equals(input.Language, "en", StringComparison.OrdinalIgnoreCase) ? "English" : "Norwegian (bokmål)");
        user.Append("Customer theme / style hint (may be very terse): ").AppendLine(
            string.IsNullOrWhiteSpace(input.ThemeDescription) ? "(none)" : input.ThemeDescription);
        user.Append("Portrait of celebrant uploaded: ").AppendLine(input.HasPortrait ? "yes — reference image attached as @image1" : "no");
        user.Append("Banner aspect ratio: ").AppendLine(input.AspectRatio);
        user.AppendLine();
        user.AppendLine("Draft starting point (the deterministic template prompt — feel free to rewrite, but stay on-topic):");
        user.AppendLine(input.BasePrompt);

        return (system.ToString(), user.ToString());
    }

    private static string StripFences(string s)
    {
        // Some chat models wrap output in ``` blocks. Strip them cheaply.
        var t = s.Trim();
        if (t.StartsWith("```"))
        {
            var firstNewline = t.IndexOf('\n');
            if (firstNewline > 0) t = t[(firstNewline + 1)..];
            if (t.EndsWith("```")) t = t[..^3];
            t = t.Trim();
        }
        return t;
    }

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    // ── OpenAI chat response DTOs ────────────────────────────────────────────

    private sealed class ChatResponse
    {
        [JsonPropertyName("choices")]
        public List<ChatChoice>? Choices { get; set; }
    }

    private sealed class ChatChoice
    {
        [JsonPropertyName("message")]
        public ChatMessage? Message { get; set; }
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
