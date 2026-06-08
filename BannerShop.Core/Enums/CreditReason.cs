namespace BannerShop.Core.Enums;

/// <summary>Reason for an AI credit transaction (grant or consume).</summary>
public enum CreditReason
{
    /// <summary>Single free generation given to an anonymous IP (rolling-30-day window).</summary>
    FreeAnonymous,

    /// <summary>Single free generation given to a newly-registered user (first ever generation).</summary>
    FreeAuthenticated,

    /// <summary>Credits granted from a purchased credit pack (29 kr / 10 credits).</summary>
    CreditPack,

    /// <summary>Credits granted automatically when a banner-print order with AI design is paid (20 credits).</summary>
    BannerOrderActivation,

    /// <summary>One credit consumed to run the AI pipeline for a generation attempt.</summary>
    Consumed,

    /// <summary>
    /// Credits granted manually by an admin (e.g. via the admin user-detail page).
    /// Logged with a null <see cref="AiCreditTransaction.ReferenceId"/> because no
    /// payment row backs the grant — i.e. these are "free" credits.
    /// </summary>
    AdminGrant,
}
