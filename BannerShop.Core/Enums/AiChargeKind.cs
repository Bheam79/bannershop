namespace BannerShop.Core.Enums;

/// <summary>
/// How the current (most recent, in-flight) AI generation attempt for a
/// <see cref="Entities.DesignRequest"/> was paid for. Recorded at the moment the
/// generation is charged (initial request or /regenerate) so that
/// <see cref="Entities.DesignRequest.LastChargeKind"/> tells the pipeline exactly what
/// to reverse if OpenAI moderation blocks the attempt (BANNERSH-288) — a moderated
/// request should not cost the customer a credit, their one-time free generation, or
/// their anonymous free try.
/// </summary>
public enum AiChargeKind
{
    /// <summary>Nothing to reverse (e.g. already reversed, or generation not yet charged).</summary>
    None = 0,

    /// <summary>Counted against the anonymous per-IP free-generation allowance.</summary>
    FreeAnonymous,

    /// <summary>Consumed the user's one-time free authenticated generation.</summary>
    FreeAuthenticated,

    /// <summary>Consumed 1 paid credit from the user's balance.</summary>
    Consumed,
}
