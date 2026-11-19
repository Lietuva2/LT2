namespace Lt2.Notifications.Digests;

/// <summary>
/// <c>notify.digest_run</c> (design 5.4, 8.19.7): one summary, prepared for review and then gone, or one sending of the
/// introduction letter. Its status says where it is (<see cref="RunStatuses"/>); a dry run is made and published, never sent.
/// </summary>
internal sealed class DigestRun
{
    public Guid Id { get; set; }

    /// <summary><c>weekly</c>, <c>before-vote</c>, <c>urgent</c> or <c>introduction</c> (design 8.19.5).</summary>
    public string Kind { get; set; } = "";

    /// <summary>The Vilnius day it goes (an urgent one: the day it was approved); its public page is <c>/suvestines/{date}</c>.</summary>
    public DateOnly RunDate { get; set; }

    /// <summary>For a before-vote summary: the sitting it is for.</summary>
    public DateOnly? SittingDate { get; set; }

    /// <summary>When it was prepared (design 8.19.2): its bills and arguments were chosen then.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When it goes: the weekly or before-vote time; for an urgent one, when it was approved.</summary>
    public DateTimeOffset? DueAt { get; set; }

    /// <summary>See <see cref="RunStatuses"/>.</summary>
    public string Status { get; set; } = RunStatuses.DryRun;

    /// <summary>
    /// The moderator who approved it (owner, 2026-11-17): "Patvirtinti" on a prepared summary marks it reviewed (it still goes
    /// at its time), and an urgent summary is approved when it is made. Never public.
    /// </summary>
    public Guid? ApprovedBy { get; set; }

    /// <summary>When it was approved; null while nobody has.</summary>
    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>"Nesiųsti" (design 8.19.2): the moderator who declined it. Never public.</summary>
    public Guid? CancelledBy { get; set; }

    /// <summary>When it was declined.</summary>
    public DateTimeOffset? CancelledAt { get; set; }

    /// <summary>The moderator's statement when it was declined ("Nesiųsti"); public on its copy.</summary>
    public string? Statement { get; set; }

    /// <summary>MPs it went to, or would have gone to as a dry run (within the ceiling, preferences and the suppression list then).</summary>
    public int MpCount { get; set; }

    /// <summary>When it went (sent or made as a dry run); its numbers and public copy are of that moment.</summary>
    public DateTimeOffset? SentAt { get; set; }

    /// <summary>How many MPs it was sent to (official recipients queued) when it went.</summary>
    public int SentCount { get; set; }

    /// <summary>For a run of kind <c>introduction</c>: the letter's version sent.</summary>
    public int? LetterVersion { get; set; }
}

/// <summary>The statuses of a run (design 8.19.1, owner 2026-11-17: no approval).</summary>
internal static class RunStatuses
{
    /// <summary>Prepared: its bills and arguments are chosen, moderators review it, it goes at <see cref="DigestRun.DueAt"/>.</summary>
    public const string Prepared = "prepared";

    /// <summary>Went while sending was off: made and published, never sent.</summary>
    public const string DryRun = "dry-run";

    public const string Sent = "sent";

    /// <summary>A moderator declined it before its time ("Nesiųsti", with a public statement): nothing went.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>At its time every bill it held was held by a moderator or no longer open: nothing went, nothing is public.</summary>
    public const string Dropped = "dropped";
}

/// <summary>
/// <c>notify.digest_choice</c> (design 8.19.2, owner 2026-11-17): an argument chosen for a prepared summary, in its place on
/// its side. Chosen by "Man svarbu" when the summary was prepared; one that left the published arguments (hidden, merged,
/// withdrawn) is marked <see cref="LeftAt"/> and the next of its side comes in (<see cref="Replacement"/>). No author.
/// </summary>
internal sealed class DigestChoice
{
    public Guid RunId { get; set; }

    public Guid ReasonId { get; set; }

    public Guid RecordId { get; set; }

    public string Side { get; set; } = "";

    /// <summary>Its order on its side as chosen: 1, 2 …; a replacement takes the next number.</summary>
    public int Place { get; set; }

    /// <summary>It came in for an argument hidden during review ("Įtrauktas vietoj paslėpto").</summary>
    public bool Replacement { get; set; }

    /// <summary>When it left the summary because it was no longer published; null while it is in.</summary>
    public DateTimeOffset? LeftAt { get; set; }
}

/// <summary><c>notify.digest_item</c>: one record in a summary, frozen as it went (design 6.6, 8.7). No person in it.</summary>
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

    /// <summary>How many published arguments each side had when the summary was made or went (design 8.4); null in a snapshot from before it was kept.</summary>
    public int? ForReasonCount { get; set; }

    public int? AgainstReasonCount { get; set; }

    /// <summary>The arguments as JSON: side, text, "Man svarbu" count; no author.</summary>
    public string Reasons { get; set; } = "[]";
}

/// <summary>
/// <c>notify.digest_delivery</c> (design 8.19.3): per run, MP and recipient (<c>official</c>, or <c>extra</c> for a confirmed
/// extra recipient), what happened (<see cref="DeliveryStatuses"/>) and the MP-specific numbers (the MP's party, groups of at
/// least 20 only). Holds no address: the address travels only in the outbox row, which is deleted when sent.
/// </summary>
internal sealed class DigestDelivery
{
    public Guid RunId { get; set; }

    public string MpSlug { get; set; } = "";

    /// <summary><c>official</c>, <c>extra</c> or <c>office</c>.</summary>
    public string Recipient { get; set; } = Recipients.Official;

    /// <summary>For an office member's copy, the member's account (design 8.19.3); <see cref="Guid.Empty"/> otherwise. Never public.</summary>
    public Guid OfficeUserId { get; set; }

    /// <summary>The MP's Seimas person id (0 on rows made before sending was built).</summary>
    public long SeimasPersonId { get; set; }

    public string Status { get; set; } = DeliveryStatuses.DryRun;

    /// <summary>Per record of the run: the MP's party numbers, as JSON; empty when no group reached 20.</summary>
    public string PartyNumbers { get; set; } = "[]";
}

internal static class Recipients
{
    public const string Official = "official";
    public const string Extra = "extra";

    /// <summary>An office member's copy (design 8.19.3, owner 2026-11-17), one row per member: <see cref="DigestDelivery.OfficeUserId"/>.</summary>
    public const string Office = "office";
}

/// <summary>
/// <c>notify.office_copy</c> (design 8.19.3, owner 2026-11-17): an office member's own choice of copies of their MP's summaries,
/// independent of the MP's (<see cref="Frequencies"/>; no row is <c>default</c>), and the hash of the member's own stop link.
/// Deleted when the office link ends or the account goes. Never public; the address is not kept here.
/// </summary>
internal sealed class OfficeCopy
{
    public Guid UserId { get; set; }

    public long SeimasPersonId { get; set; }

    public string Frequency { get; set; } = Frequencies.Default;

    /// <summary>Counts the stop links made, so each is a new token.</summary>
    public int Generation { get; set; }

    /// <summary>SHA-256 of the member's current stop token, hex; set when the first copy is queued.</summary>
    public string? StopTokenHash { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>What happened to one delivery (design 8.19.3). <c>dry-run</c>: the run went while sending was off.</summary>
internal static class DeliveryStatuses
{
    public const string DryRun = "dry-run";
    public const string Queued = "queued";
    public const string Sent = "sent";
    public const string Failed = "failed";
    public const string Bounced = "bounced";
    public const string SkippedCeiling = "skipped-ceiling";
    public const string SkippedPreference = "skipped-preference";
    public const string Suppressed = "suppressed";
    public const string Introduced = "introduced";

    /// <summary>A delivery that went to the outbox (and may since have been sent, failed or bounced).</summary>
    public static readonly IReadOnlyList<string> Went = [Queued, Sent, Failed, Bounced];
}

/// <summary>
/// <c>notify.mp_email_pref</c> (design 8.19.4): per Seimas person, how often the MP wants summaries, the extra recipient and
/// its confirmation. Never public; whether an MP stopped the summaries is shown to nobody but that MP.
/// </summary>
internal sealed class MpEmailPref
{
    public long SeimasPersonId { get; set; }

    /// <summary>See <see cref="Frequencies"/>.</summary>
    public string Frequency { get; set; } = Frequencies.Default;

    /// <summary>The extra recipient's address as written; deleted when removed. Gets copies only once confirmed.</summary>
    public string? ExtraAddress { get; set; }

    /// <summary>When the official address confirmed the extra recipient; null while it waits.</summary>
    public DateTimeOffset? ExtraConfirmedAt { get; set; }

    /// <summary>Counts the preferences links made, so a new one ("Atsiųsti naują nuorodą") is a new token.</summary>
    public int TokenGeneration { get; set; }

    /// <summary>Counts the extra recipients confirmed, so each has its own "Nebegauti kopijų" link.</summary>
    public int ExtraGeneration { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>The choices of the preferences page (design 8.19.4). An urgent summary counts as one before votes.</summary>
internal static class Frequencies
{
    public const string Default = "default";
    public const string Weekly = "weekly";
    public const string BeforeVote = "before-vote";
    public const string Stopped = "stopped";

    public static readonly IReadOnlyList<string> All = [Default, Weekly, BeforeVote, Stopped];
}

/// <summary>
/// <c>notify.mp_mail_token</c> (design 8.19.4): a hashed token per MP and kind. <c>preferences</c> opens the MP's page and
/// the one-click stop and never expires; <c>extra-confirm</c> confirms an extra recipient (24 hours, one use);
/// <c>extra-stop</c> is the copies' own "Nebegauti kopijų".
/// </summary>
internal sealed class MpMailToken
{
    public Guid Id { get; set; }

    public long SeimasPersonId { get; set; }

    public string Kind { get; set; } = TokenKinds.Preferences;

    /// <summary>SHA-256 of the token, hex.</summary>
    public string TokenHash { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? UsedAt { get; set; }
}

internal static class TokenKinds
{
    public const string Preferences = "preferences";
    public const string ExtraConfirm = "extra-confirm";
    public const string ExtraStop = "extra-stop";
}

/// <summary>
/// <c>notify.introduction_letter</c> (design 8.19.5): one version of the letter written and signed by a person. A version
/// once sent is never changed: a changed text is a new version.
/// </summary>
internal sealed class IntroductionLetter
{
    public int Version { get; set; }

    public string Subject { get; set; } = "";

    /// <summary>Plain paragraphs, separated by an empty line.</summary>
    public string Text { get; set; } = "";

    public string SignerName { get; set; } = "";

    public string SignerRole { get; set; } = "";

    /// <summary>The administrator who wrote this version. Never public.</summary>
    public Guid WrittenBy { get; set; }

    public DateTimeOffset WrittenAt { get; set; }

    /// <summary>When this version was first sent to MPs; null while it is a draft.</summary>
    public DateTimeOffset? SentAt { get; set; }
}

/// <summary>
/// <c>notify.suppression</c> (design 8.19.6): an address MP mail never goes to again, as a keyed hash; never the address.
/// </summary>
internal sealed class Suppression
{
    public Guid Id { get; set; }

    /// <summary>HMAC-SHA-256 of the address in lower case under the mail key, hex.</summary>
    public string AddressHash { get; set; } = "";

    /// <summary><c>bounced</c>, <c>complaint</c> or <c>staff</c>.</summary>
    public string Reason { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The administrator who added it by hand; null when it was added automatically.</summary>
    public Guid? AddedBy { get; set; }

    /// <summary>The administrator's statement; null when it was added automatically.</summary>
    public string? Statement { get; set; }
}
