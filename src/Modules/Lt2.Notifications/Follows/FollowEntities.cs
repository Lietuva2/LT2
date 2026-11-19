namespace Lt2.Notifications.Follows;

/// <summary>
/// <c>notify.follow</c> (design 5.4, 8.11.2, 8.14.14): a member follows a bill (key: its number), an MP (key: the Seimas
/// person id) or an organisation (key: its id in the Appeals module). Private: shown to nobody but the person; exported and
/// deleted with the account; never combined with sides or invitations.
/// </summary>
internal sealed class Follow
{
    public Guid UserId { get; set; }

    /// <summary><see cref="FollowKinds.Bill"/>, <see cref="FollowKinds.Mp"/> or <see cref="FollowKinds.Organisation"/>.</summary>
    public string Kind { get; set; } = "";

    public string Key { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// A followed bill only (design 8.11.8): when the person last opened the bill's page, from the follow's own moment; what was
    /// written after it is new to them. Nothing about any other page; goes with the follow.
    /// </summary>
    public DateTimeOffset LastSeenAt { get; set; }
}

internal static class FollowKinds
{
    public const string Bill = "bill";
    public const string Mp = "mp";
    public const string Organisation = "organisation";

    /// <summary>A member with a public profile (design 8.25.5); key: the public profile id. Ended when it is switched off.</summary>
    public const string Member = "member";
}

/// <summary><c>notify.notice_setting</c>: which follow emails a person gets; no row means all on (design 8.11.2, 8.14.14, 8.20.1).</summary>
internal sealed class NoticeSetting
{
    public Guid UserId { get; set; }

    public bool Planned { get; set; } = true;

    public bool Voted { get; set; } = true;

    /// <summary>An organisation the person follows made a statement public (design 8.14.14, stage B).</summary>
    public bool Statements { get; set; } = true;

    /// <summary>MPs answered on a bill the person follows (design 8.20.1, "Seimo narių atsakymai").</summary>
    public bool Responses { get; set; } = true;

    /// <summary>A member the person follows published an argument (design 8.25.5, "Kai sekamas narys paskelbia").</summary>
    public bool Members { get; set; } = true;

    /// <summary>How often the emails come (design 8.24): one of <see cref="Rhythm.Frequencies"/>; no row means "Iš karto".</summary>
    public string Frequency { get; set; } = Rhythm.Frequencies.Immediate;

    /// <summary>When <see cref="Frequency"/> was chosen; null: never chosen, so it holds for every event.</summary>
    public DateTimeOffset? FrequencySince { get; set; }

    /// <summary>The choice before <see cref="Frequency"/>, for the events seen while it held (design 8.24, a change of the choice).</summary>
    public string? PreviousFrequency { get; set; }

    /// <summary>When <see cref="PreviousFrequency"/> was chosen; null: it held from the start.</summary>
    public DateTimeOffset? PreviousSince { get; set; }

    /// <summary>Someone commented under the person's own argument (design 8.23.9, "Kai mano argumentą pakomentuoja").</summary>
    public bool Comments { get; set; } = true;

    public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>
/// <c>notify.member_mail_token</c> (design 8.24.1): the member's stop link, one per account. The token is derived from the account
/// id and <see cref="Generation"/> under <c>Security:MailHashKey</c> (<c>MailKeys.MemberStopToken</c>), so every email carries the
/// same link; this row keeps only its SHA-256, made when the first email of the rhythm is written. No expiry; deleted with the
/// account; not in the export (a key, not the person's data); withheld from <c>lt2_readonly</c>.
/// </summary>
internal sealed class MemberMailToken
{
    public Guid UserId { get; set; }

    /// <summary>The token's SHA-256, hex (<c>MailKeys.TokenHash</c>).</summary>
    public string TokenHash { get; set; } = "";

    /// <summary>The token's generation: 0; a later "new link" would move it on, and the old link would stop working.</summary>
    public int Generation { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// <c>notify.bill_event</c> (design 5.4, 8.11.2): what followers are told about, recorded once, when the job first sees it:
/// a planned date (key: the date), a stage-result vote (key: the vote id), or an organisation's statement on the bill that
/// became public (key: the statement's id, design 8.14.14 stage B), or an MP's response whose stance became public (key: the person number and that moment, design 8.20.1, 8.24).
/// No person in it.
/// </summary>
internal sealed class BillEvent
{
    public string BillNumber { get; set; } = "";

    /// <summary>One of <see cref="BillEventKinds"/>.</summary>
    public string Kind { get; set; } = "";

    public string Key { get; set; } = "";

    public DateTimeOffset FirstSeen { get; set; }

    /// <summary>When its emails went to the outbox; null while waiting or when it is never sent.</summary>
    public DateTimeOffset? SentAt { get; set; }

    /// <summary>See <see cref="BillEventOutcomes"/>: the mark of the runs' emails ("Iš karto", design 8.24).</summary>
    public string Outcome { get; set; } = BillEventOutcomes.Waiting;

    /// <summary>When a daily email (design 8.24) took the event, whoever it went to; null until then.</summary>
    public DateTimeOffset? DailyAt { get; set; }

    /// <summary>When a weekly email (design 8.24) took the event; null until then.</summary>
    public DateTimeOffset? WeeklyAt { get; set; }

    /// <summary>When the job decided whether a "planned" event could wait for the gathered emails (design 8.24); null until then.</summary>
    public DateTimeOffset? EarlyAt { get; set; }
}

internal static class BillEventKinds
{
    public const string Planned = "planned";
    public const string Voted = "voted";
    public const string Statement = "statement";

    /// <summary>An MP's response on the bill became public, or changed stance (design 8.20.1, 8.24; key: <see cref="FollowRules.ResponseKey"/>). No member and no text.</summary>
    public const string Response = "response";

    /// <summary>A followed member published an argument or an own-voice comment on the bill (design 8.25.5; key: its id). No person, no text.</summary>
    public const string Member = "member";

    /// <summary>
    /// A moderator closed the bill's record because the Seimas rejected it or its initiators withdrew it (design 8.1 step 6, 8.6;
    /// key: <see cref="FollowRules.ClosedKey"/>, the moment it closed). No person.
    /// </summary>
    public const string Closed = "closed";

    /// <summary>
    /// An argument on the bill got comments and its author was told (design 8.23.9, 8.23.14; key: the argument's id and the run's
    /// moment). No person and no text; the latest one's <see cref="BillEvent.FirstSeen"/> is where the next run starts reading.
    /// </summary>
    public const string Comment = "comment";
}

internal static class BillEventOutcomes
{
    /// <summary>To be sent: at once, or a planned date waiting for the next Vilnius day.</summary>
    public const string Waiting = "waiting";

    public const string Sent = "sent";

    /// <summary>Recorded and never sent: a planned date already past, or a vote older than three days when first seen.</summary>
    public const string Skipped = "skipped";

    /// <summary>A waiting planned date that moved again before it was sent; the date current then is sent instead.</summary>
    public const string Superseded = "superseded";
}
