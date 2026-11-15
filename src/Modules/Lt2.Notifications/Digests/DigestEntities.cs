namespace Lt2.Notifications.Digests;

/// <summary><c>notify.digest_run</c> (design 5.4): one summary as made. In M2a every run is a dry run: nothing is sent.</summary>
internal sealed class DigestRun
{
    public Guid Id { get; set; }

    /// <summary><c>weekly</c>, <c>before-vote</c> or <c>urgent</c>.</summary>
    public string Kind { get; set; } = "";

    /// <summary>The Vilnius day it was made; its public page is <c>/suvestines/{date}</c>.</summary>
    public DateOnly RunDate { get; set; }

    /// <summary>For a before-vote summary: the sitting it is for.</summary>
    public DateOnly? SittingDate { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary><c>dry-run</c> in M2a.</summary>
    public string Status { get; set; } = "dry-run";

    /// <summary>For an urgent summary: the moderator who approved it.</summary>
    public Guid? ApprovedBy { get; set; }

    /// <summary>MPs it would have gone to (within the ceiling).</summary>
    public int MpCount { get; set; }
}

/// <summary><c>notify.digest_item</c>: one record in a summary, frozen as it was (design 6.6, 8.7). No person in it.</summary>
internal sealed class DigestItem
{
    public Guid Id { get; set; }

    public Guid RunId { get; set; }

    public int Position { get; set; }

    public Guid RecordId { get; set; }

    public string BillNumber { get; set; } = "";

    public string BillTitle { get; set; } = "";

    public string? Stage { get; set; }

    public DateOnly? PlannedDate { get; set; }

    public int ForCount { get; set; }

    public int ForOlderThan30Days { get; set; }

    public int AgainstCount { get; set; }

    public int AgainstOlderThan30Days { get; set; }

    public int? SurgeSharePercent { get; set; }

    public int? SurgeWindowHours { get; set; }

    /// <summary>The arguments as JSON: side, text, "Man svarbu" count; no author.</summary>
    public string Reasons { get; set; } = "[]";
}

/// <summary>
/// <c>notify.digest_delivery</c>: per run and MP, what happened (here <c>dry-run</c> or <c>skipped-ceiling</c>) and the
/// MP-specific numbers (the MP's party, groups of at least 20 only). Holds no address: M2a never sends.
/// </summary>
internal sealed class DigestDelivery
{
    public Guid RunId { get; set; }

    public string MpSlug { get; set; } = "";

    public string Status { get; set; } = "dry-run";

    /// <summary>Per record of the run: the MP's party numbers, as JSON; empty when no group reached 20.</summary>
    public string PartyNumbers { get; set; } = "[]";
}
