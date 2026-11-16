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

    public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>
/// <c>notify.bill_event</c> (design 5.4, 8.11.2): what followers are told about, recorded once, when the job first sees it:
/// a planned date (key: the date), a stage-result vote (key: the vote id), or an organisation's statement on the bill that
/// became public (key: the statement's id, design 8.14.14 stage B), or MPs' responses of one Vilnius day (key: the day, design 8.20.1).
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

    /// <summary>See <see cref="BillEventOutcomes"/>.</summary>
    public string Outcome { get; set; } = BillEventOutcomes.Waiting;
}

internal static class BillEventKinds
{
    public const string Planned = "planned";
    public const string Voted = "voted";
    public const string Statement = "statement";

    /// <summary>MPs answered on the bill on one Vilnius day (design 8.20.1; key: the day). No person and no text.</summary>
    public const string Response = "response";

    /// <summary>A followed member published an argument on the bill (design 8.25.5; key: the argument id). No person, no text.</summary>
    public const string Member = "member";
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
