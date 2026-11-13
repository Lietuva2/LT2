namespace Lt2.Appeals.Domain;

/// <summary>
/// Several proposals per bill, one published appeal (design 8.1, decided 2026-11-13). An appeal row is a proposal until
/// a moderator publishes it. Each rule returns the error code a request gets when it breaks the rule, or <c>null</c>.
/// </summary>
internal static class ProposalRules
{
    /// <summary>An open or closed appeal is the bill's published appeal; at most one per bill.</summary>
    public static bool IsPublished(string status) => status is AppealStatuses.Open or AppealStatuses.Closed;

    /// <summary>A pending or returned proposal is still in play: it waits for a moderator or for its author's correction.</summary>
    public static bool IsWaiting(string status) => status is AppealStatuses.Pending or AppealStatuses.Returned;

    /// <summary>
    /// A member may propose an appeal on a bill unless the bill has a published appeal (<c>appeal.exists</c>: they are
    /// taken to it) or they already have a waiting or returned proposal on it (<c>proposal.exists</c>: they are taken to
    /// their own). Someone else's proposal, a hidden appeal or a closed proposal never blocks anyone.
    /// </summary>
    public static string? CheckNewProposal(IEnumerable<(string Status, bool Mine)> appealsOnTheBill)
    {
        var all = appealsOnTheBill.ToList();
        if (all.Any(a => IsPublished(a.Status)))
        {
            return "appeal.exists";
        }

        return all.Any(a => a.Mine && IsWaiting(a.Status)) ? "proposal.exists" : null;
    }

    /// <summary>How many proposals wait for a moderator on a bill: waiting and returned ones; the number only.</summary>
    public static int Waiting(IEnumerable<string> statusesOnTheBill) => statusesOnTheBill.Count(IsWaiting);

    /// <summary>
    /// A moderator publishes a waiting proposal only while no other appeal on the bill is published
    /// (<c>appeal.published-exists</c>).
    /// </summary>
    public static string? CheckPublish(string status, IEnumerable<string> otherStatusesOnTheBill)
    {
        if (status != AppealStatuses.Pending)
        {
            return "moderation.wrong-state";
        }

        return otherStatusesOnTheBill.Any(IsPublished) ? "appeal.published-exists" : null;
    }

    /// <summary>Publishing one proposal closes every other waiting or returned proposal on the bill.</summary>
    public static bool ClosedWhenAnotherIsPublished(string status) => IsWaiting(status);

    /// <summary>
    /// The status a hidden appeal comes back with (design 8.3): closed if it had closed, open if it had been published,
    /// otherwise a waiting proposal.
    /// </summary>
    public static string RestoredStatus(DateTimeOffset? publishedAt, DateTimeOffset? closedAt) =>
        closedAt is not null ? AppealStatuses.Closed
        : publishedAt is not null ? AppealStatuses.Open
        : AppealStatuses.Pending;

    /// <summary>
    /// Restoring a hidden appeal (design 8.3): only a hidden one; never while another appeal on the bill is published
    /// (<c>appeal.published-exists</c>); one hidden before publication not while its author has another waiting or
    /// returned proposal on the bill (<c>proposal.exists</c>). Restoring a published one while proposals wait is allowed
    /// and counts as publishing (they close).
    /// </summary>
    public static string? CheckRestore(string status, string restoredStatus, IEnumerable<(string Status, bool SameAuthor)> othersOnTheBill)
    {
        if (status != AppealStatuses.Rejected)
        {
            return "moderation.wrong-state";
        }

        var others = othersOnTheBill.ToList();
        if (others.Any(o => IsPublished(o.Status)))
        {
            return "appeal.published-exists";
        }

        return restoredStatus == AppealStatuses.Pending && others.Any(o => o.SameAuthor && IsWaiting(o.Status)) ? "proposal.exists" : null;
    }
}
