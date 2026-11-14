namespace Lt2.Notifications;

/// <summary>Sends due outbox messages. Called in a loop by the worker.</summary>
public interface INotificationsDispatcher
{
    /// <summary>Dispatches one batch of due messages and returns how many were handled (sent or failed).</summary>
    Task<int> DispatchDueAsync(CancellationToken cancellationToken);
}

/// <summary>Which sending stream a message belongs to, so a problem with one cannot block the other (design 8.4).</summary>
public enum EmailCategory
{
    /// <summary>Sign-in codes and other messages a person asked for.</summary>
    Transactional,

    /// <summary>Summaries to MPs.</summary>
    Digest,
}

/// <summary>An email ready to send: plain text always, light HTML optionally.</summary>
public sealed record EmailMessage(string To, string Subject, string TextBody, string? HtmlBody, EmailCategory Category);

/// <summary>Delivers email. SMTP today; the provider is chosen later (design section 12).</summary>
public interface IEmailTransport
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>One appeal as frozen in a summary. No person: counts, the surge note and arguments without authors.</summary>
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

/// <summary>Who approves an urgent summary, for the audit log.</summary>
public sealed record StaffActor(Guid UserId, string Role, string? IpHash);

/// <summary>Summaries to MPs in dry-run (design 8.4, 8.7, 8.9). Nothing here sends anything.</summary>
public interface IDigests
{
    /// <summary>Makes the weekly and before-vote summaries that are due at <paramref name="now"/>; returns those made.</summary>
    Task<IReadOnlyList<DigestRunInfo>> RunDueAsync(DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>One urgent summary for an appeal, approved by a moderator; may exceed the weekly ceiling by one.</summary>
    Task<Lt2.SharedKernel.Result<DigestRunInfo>> UrgentAsync(Guid appealId, string statement, StaffActor moderator, CancellationToken cancellationToken);

    Task<IReadOnlyList<DigestRunInfo>> ListAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<DigestRunInfo>> OnDateAsync(DateOnly date, CancellationToken cancellationToken);

    /// <summary>The summaries that held one appeal, newest first; each run carries only that appeal's frozen item.</summary>
    Task<IReadOnlyList<DigestRunInfo>> ForAppealAsync(Guid appealId, CancellationToken cancellationToken);

    Task<Lt2.SharedKernel.Result<DigestPreview>> PreviewAsync(Guid runId, string mpSlug, CancellationToken cancellationToken);

    /// <summary>The MPs of a run and what would have happened for each (<c>dry-run</c> or <c>skipped-ceiling</c>); moderators only.</summary>
    Task<IReadOnlyList<(string MpSlug, string Status)>> MpsAsync(Guid runId, CancellationToken cancellationToken);
}

/// <summary>A bill the person follows, with its planned date or its last stage result.</summary>
public sealed record FollowedBill(Lt2.SharedKernel.BillFacts Bill, Lt2.SharedKernel.PlannedDate? Planned, Lt2.SharedKernel.StageVoteFacts? LastStageResult);

/// <summary>The person's own follows (design 8.11.2). <c>Max</c>: how many bills and MPs together may be followed.</summary>
public sealed record MyFollows(IReadOnlyList<FollowedBill> Bills, IReadOnlyList<Lt2.SharedKernel.MpFacts> Mps, int Max);

/// <summary>Which follow emails are on.</summary>
public sealed record NoticeSettings(bool Planned, bool Voted);

/// <summary>What one run of the follow job did: events recorded, events sent, emails written to the outbox.</summary>
public sealed record FollowRunOutcome(int Recorded, int Sent, int Emails);

/// <summary>
/// Following bills and MPs (design 8.11.2). Every operation is the person's own: nothing here answers who follows what,
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

    Task<NoticeSettings> NoticesAsync(Guid userId, CancellationToken cancellationToken);

    Task<Lt2.SharedKernel.Result<Lt2.SharedKernel.Unit>> SetNoticesAsync(Guid userId, NoticeSettings settings, CancellationToken cancellationToken);

    /// <summary>
    /// The worker job (every 15 minutes): records new planned dates and stage votes of followed bills and emails the
    /// followers by the rules of design 8.11.2, through the outbox.
    /// </summary>
    Task<FollowRunOutcome> RunAsync(DateTimeOffset now, CancellationToken cancellationToken);
}