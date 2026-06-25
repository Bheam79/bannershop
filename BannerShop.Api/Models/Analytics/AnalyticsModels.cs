namespace BannerShop.Api.Models.Analytics;

/// <summary>Request body for recording a single page-view event.</summary>
public record TrackPageViewRequest(
    string SessionId,
    bool IsNewSession,
    string Path,
    string? Referrer
);

/// <summary>One time-bucket in the sessions-over-time chart.</summary>
public record SessionBucket(
    string Label,        // e.g. "2026-06-25" or "2026-06-25 14:00"
    DateTime BucketUtc, // UTC start of the bucket
    int PageViews,
    int UniqueSessions,
    int NewSessions
);

/// <summary>Overall traffic stats summary.</summary>
public record TrafficSummary(
    int TotalPageViews,
    int TotalSessions,
    int NewSessions,
    IReadOnlyList<SessionBucket> Buckets
);

/// <summary>Referrer source count for the pie/bar chart.</summary>
public record ReferrerStat(
    string Source,       // Direct | Google | Facebook | Instagram | Other
    int Sessions,
    int PageViews
);

public record ReferrerStats(IReadOnlyList<ReferrerStat> Sources);
