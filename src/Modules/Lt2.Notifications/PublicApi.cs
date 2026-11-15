using Lt2.SharedKernel;

namespace Lt2.Notifications;

/// <summary>Sends due outbox messages. Called in a loop by the worker.</summary>
public interface INotificationsDispatcher
{
    /// <summary>
    /// Dispatches one batch of due messages, sign-in and confirmation codes first (design 6.8), over one connection, and
    /// returns how many were handled (sent or failed).
    /// </summary>
    Task<int> DispatchDueAsync(CancellationToken cancellationToken);

    /// <summary>Deletes messages that gave up more than 30 days ago (design 6.8: the outbox forgets); returns how many.</summary>
    Task<int> PurgeFailedAsync(CancellationToken cancellationToken);
}

/// <summary>An email ready to send: plain text always, light HTML optionally.</summary>
public sealed record EmailMessage(string To, string Subject, string TextBody, string? HtmlBody);

/// <summary>Delivers email. SMTP today; the provider is chosen later (design section 12).</summary>
public interface IEmailTransport
{
    /// <summary>Sends one message on a connection of its own.</summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// A connection for one batch: every message sent through it shares it. Without an override each message is sent on
    /// its own (<see cref="SendAsync"/>).
    /// </summary>
    Task<IEmailConnection> ConnectAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IEmailConnection>(new Transports.MessageByMessage(this));
}

/// <summary>A connection to the mail server, held for one batch of messages.</summary>
public interface IEmailConnection : IAsyncDisposable
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>One record as frozen in a summary. No person: counts, the surge note and arguments without authors.</summary>
public sealed record DigestItemInfo(
    string BillNumber,
    string BillTitle,
    string? Stage,
    DateOnly? PlannedDate,
    int For,
    int ForOlderThan30Days,
    int Against,
    int AgainstOlderThan30Days,
    int? SurgeSharePercent,
    int? SurgeWindowHours,
    IReadOnlyList<Lt2.SharedKernel.DigestReason> Reasons);

/// <summary>A summary run (design 8.7) as its public copy shows it: without MP-specific figures.</summary>
/// <param name="Status"><c>dry-run</c> in M2a: made, read by people, never sent.</param>
/// <param name="MpCount">How many MPs it would have gone to.</param>
public sealed record DigestRunInfo(
    Guid Id, string Kind, DateOnly RunDate, DateOnly? SittingDate, DateTimeOffset CreatedAt, string Status, int MpCount, IReadOnlyList<DigestItemInfo> Items);

/// <summary>What one MP would read (design 8.9: a moderator can read any MP's summary before a real send).</summary>
public sealed record DigestPreview(string Subject, string Text);


/// <summary>Summaries to MPs in dry-run (design 8.4, 8.7, 8.9). Nothing here sends anything.</summary>
public interface IDigests
{
    /// <summary>Makes the weekly and before-vote summaries that are due at <paramref name="now"/>; returns those made.</summary>
    Task<IReadOnlyList<DigestRunInfo>> RunDueAsync(DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>One urgent summary for a record, approved by a moderator; may exceed the weekly ceiling by one.</summary>
    Task<Lt2.SharedKernel.Result<DigestRunInfo>> UrgentAsync(Guid recordId, string statement, StaffActor moderator, CancellationToken cancellationToken);

    Task<IReadOnlyList<DigestRunInfo>> ListAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<DigestRunInfo>> OnDateAsync(DateOnly date, CancellationToken cancellationToken);

    /// <summary>The summaries that held one record, newest first; each run carries only that record's frozen item.</summary>
    Task<IReadOnlyList<DigestRunInfo>> ForRecordAsync(Guid recordId, CancellationToken cancellationToken);

    Task<Lt2.SharedKernel.Result<DigestPreview>> PreviewAsync(Guid runId, string mpSlug, CancellationToken cancellationToken);

    /// <summary>The MPs of a run and what would have happened for each (<c>dry-run</c> or <c>skipped-ceiling</c>); moderators only.</summary>
    Task<IReadOnlyList<(string MpSlug, string Status)>> MpsAsync(Guid runId, CancellationToken cancellationToken);
}

/// <summary>A bill the person follows, with its planned date or its last stage result.</summary>
public sealed record FollowedBill(Lt2.SharedKernel.BillFacts Bill, Lt2.SharedKernel.PlannedDate? Planned, Lt2.SharedKernel.StageVoteFacts? LastStageResult);

/// <summary>The person's own follows (design 8.11.2, 8.14.14). <c>Max</c>: how many bills, MPs and organisations together may be followed.</summary>
public sealed record MyFollows(IReadOnlyList<FollowedBill> Bills, IReadOnlyList<Lt2.SharedKernel.MpFacts> Mps, int Max, IReadOnlyList<Lt2.SharedKernel.OrganisationFacts> Organisations);

/// <summary>Which follow emails are on: planned and voted bills (design 8.11.2), organisations' statements (design 8.14.14).</summary>
public sealed record NoticeSettings(bool Planned, bool Voted, bool Statements = true);

/// <summary>What one run of the follow job did: events recorded, events sent, emails written to the outbox.</summary>
public sealed record FollowRunOutcome(int Recorded, int Sent, int Emails);

/// <summary>
/// Following bills, MPs (design 8.11.2) and organisations (design 8.14.14). Every operation is the person's own: nothing here answers who follows what,
/// and nothing is combined with sides or invitations.
/// </summary>
public interface IFollows
{
    Task<MyFollows> MineAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Follows a bill: <c>bill.not-found</c>, <c>member.required</c>, <c>follow.limit</c>; following again changes nothing.</summary>
    Task<Lt2.SharedKernel.Result<Lt2.SharedKernel.Unit>> FollowBillAsync(Guid userId, string number, CancellationToken cancellationToken);

    Task<Lt2.SharedKernel.Result<Lt2.SharedKernel.Unit>> UnfollowBillAsync(Guid userId, string number, CancellationToken cancellationToken);

    /// <summary>Follows an MP by profile slug: <c>mp.not-found</c>, <c>member.required</c>, <c>follow.limit</c>.</summary>
    Task<Lt2.SharedKernel.Result<Lt2.SharedKernel.Unit>> FollowMpAsync(Guid userId, string slug, CancellationToken cancellationToken);

    Task<Lt2.SharedKernel.Result<Lt2.SharedKernel.Unit>> UnfollowMpAsync(Guid userId, string slug, CancellationToken cancellationToken);

    /// <summary>
    /// Follows an active organisation by its profile slug (design 8.14.14, stage B): <c>organisation.not-found</c> (unknown,
    /// blocked or closed), <c>member.required</c>, <c>follow.limit</c>.
    /// </summary>
    Task<Lt2.SharedKernel.Result<Lt2.SharedKernel.Unit>> FollowOrganisationAsync(Guid userId, string slug, CancellationToken cancellationToken);

    Task<Lt2.SharedKernel.Result<Lt2.SharedKernel.Unit>> UnfollowOrganisationAsync(Guid userId, string slug, CancellationToken cancellationToken);

    Task<NoticeSettings> NoticesAsync(Guid userId, CancellationToken cancellationToken);

    Task<Lt2.SharedKernel.Result<Lt2.SharedKernel.Unit>> SetNoticesAsync(Guid userId, NoticeSettings settings, CancellationToken cancellationToken);

    /// <summary>
    /// The worker job (every 15 minutes): records new planned dates and stage votes of followed bills, and statements of
    /// followed organisations that became public, and emails the followers by the rules of design 8.11.2 and 8.14.14,
    /// through the outbox.
    /// </summary>
    Task<FollowRunOutcome> RunAsync(DateTimeOffset now, CancellationToken cancellationToken);
}