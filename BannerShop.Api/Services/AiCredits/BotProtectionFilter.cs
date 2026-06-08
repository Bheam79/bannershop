using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BannerShop.Api.Services.AiCredits;

/// <summary>
/// Lightweight bot-protection action filter for anonymous AI generation endpoints.
/// Rejects requests that:
///   • have a missing or obviously-bot User-Agent, or
///   • are missing the <c>X-Request-Integrity</c> header (a non-empty browser fingerprint token).
///
/// Apply via <c>[ServiceFilter(typeof(BotProtectionFilter))]</c> on controller actions.
/// </summary>
public sealed class BotProtectionFilter : IAsyncActionFilter
{
    private static readonly HashSet<string> BotPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "Googlebot", "Bingbot", "Slurp", "DuckDuckBot", "Baiduspider",
        "YandexBot", "Sogou", "Exabot", "facebot", "ia_archiver",
        "python-requests", "python-urllib", "curl", "wget", "libwww",
        "scrapy", "axios/", "node-fetch", "go-http-client",
        "Playwright", "HeadlessChrome", "PhantomJS", "Selenium",
        "puppeteer", "cypress",
    };

    private readonly ILogger<BotProtectionFilter> _log;

    public BotProtectionFilter(ILogger<BotProtectionFilter> log)
    {
        _log = log;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        var ua = request.Headers.UserAgent.ToString();

        // Reject if User-Agent is absent or blank.
        if (string.IsNullOrWhiteSpace(ua))
        {
            _log.LogWarning("BotProtection: rejected request with missing User-Agent from {Ip}.",
                context.HttpContext.Connection.RemoteIpAddress);
            context.Result = new ObjectResult(new { error = "bot_suspected" }) { StatusCode = 403 };
            return;
        }

        // Reject if User-Agent matches a known bot/crawler/headless pattern.
        if (BotPatterns.Any(p => ua.Contains(p, StringComparison.OrdinalIgnoreCase)))
        {
            _log.LogWarning("BotProtection: rejected bot UA '{UA}' from {Ip}.", ua,
                context.HttpContext.Connection.RemoteIpAddress);
            context.Result = new ObjectResult(new { error = "bot_suspected" }) { StatusCode = 403 };
            return;
        }

        // Reject if X-Request-Integrity header is absent or empty.
        var integrity = request.Headers["X-Request-Integrity"].ToString();
        if (string.IsNullOrWhiteSpace(integrity))
        {
            _log.LogWarning("BotProtection: rejected request missing X-Request-Integrity from {Ip}.",
                context.HttpContext.Connection.RemoteIpAddress);
            context.Result = new ObjectResult(new { error = "bot_suspected" }) { StatusCode = 403 };
            return;
        }

        await next();
    }
}
