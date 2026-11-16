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
}

internal static class FollowKinds
{
    public const string Bill = "bill";
    public const string Mp = "mp";
    public const string Organisation = "organisation";
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

    /// <summary>How often the emails come (design 8.24): one of <see cref="Rhythm.Frequencies"/>; no row means "Iš karto".</summary>
    public string Frequency { get; set; } = Rhythm.Frequencies.Immediate;

    /// <summary>When <see cref="Frequency"/> was chosen; null: never chosen, so it holds for every event.</summary>
    public DateTimeOffset? FrequencySince { get; set; }

    /// <summary>The choice before <see cref="Frequency"/>, for the events seen while it held (design 8.24, a change of the choice).</summary>
    public string? PreviousFrequency { get; set; }

    /// <summary>When <see cref="PreviousFrequency"/> was chosen; null: it held from the start.</summary>
    public DateTimeOffset? PreviousSince { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
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
