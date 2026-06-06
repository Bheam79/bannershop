namespace BannerShop.Core.Enums;

/// <summary>
/// Lifecycle state of a <see cref="Entities.DesignRequest"/>.
///
/// Happy path AI:  Pending → InProgress → AwaitingApproval → Approved → Final
/// Happy path Man: Pending → InProgress → AwaitingApproval → Approved → Final
/// Revisions:                              ↳ RevisionRequested → Revised → AwaitingApproval …
/// Either path can short-circuit to <see cref="Cancelled"/>.
///
/// Stored as a string in the DB.
/// </summary>
public enum DesignRequestStatus
{
    Pending = 1,            // created, awaiting payment
    InProgress = 2,         // payment confirmed, AI pipeline running OR designer working
    AwaitingApproval = 3,   // preview produced, customer can approve / request revision
    Approved = 4,           // customer accepted the preview
    RevisionRequested = 5,  // customer asked for changes (Manual)
    Revised = 6,            // designer uploaded a new revision (Manual)
    Final = 7,              // final asset delivered (post-approval state)
    Failed = 8,             // pipeline failed irrecoverably (kept for traceability)
    Cancelled = 9
}
