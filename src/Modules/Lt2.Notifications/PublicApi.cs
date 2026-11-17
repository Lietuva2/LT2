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
/// <param name="Stream">
/// <c>null</c> for the common stream; <c>mp</c> for MP mail (design 8.19.3), sent from <c>Email:Mp:FromAddress</c> and, when
/// configured, over <c>Email:Mp:Smtp</c>, so trouble with it cannot block sign-in codes.
/// </param>
/// <param name="Headers">Extra headers, for example <c>List-Unsubscribe</c> and <c>List-Unsubscribe-Post</c>.</param>
public sealed record EmailMessage(
    string To, string Subject, string TextBody, string? HtmlBody, string? Stream = null, IReadOnlyDictionary<string, string>? Headers = null);

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

/// <summary>A summary run (design 8.7, 8.19.2) as its public copy shows it: without MP-specific figures and never which MPs.</summary>
/// <param name="Status"><c>dry-run</c>, <c>waiting</c>, <c>sent</c>, <c>cancelled</c> or <c>expired</c> (design 8.19.1).</param>
/// <param name="MpCount">How many MPs it would go to when it was made.</param>
/// <param name="SentCount">How many MPs it was sent to (a sent run).</param>
/// <param name="DecidedAt">When it was sent, declined or expired.</param>
/// <param name="Statement">A declined run's statement, shown on its copy; never who declined it.</param>
/// <param name="ExpiresAt">A waiting run's expiry.</param>
public sealed record DigestRunInfo(
    Guid Id,
    string Kind,
    DateOnly RunDate,
    DateOnly? SittingDate,
    DateTimeOffset CreatedAt,
    string Status,
    int MpCount,
    IReadOnlyList<DigestItemInfo> Items,
    int SentCount = 0,
    DateTimeOffset? DecidedAt = null,
    string? Statement = null,
    DateTimeOffset? ExpiresAt = null);

/// <summary>What one MP would read (design 8.9: a moderator can read any MP's summary before a real send).</summary>
public sealed record DigestPreview(string Subject, string Text);

/// <summary>
/// Sending as it is now (design 8.19.1): <paramref name="Mode"/> <c>off</c>, <c>approval</c> or <c>automatic</c>;
/// <paramref name="Setting"/> what <c>Digest:Sending</c> says; <paramref name="Lock"/> why it is off although the setting says
/// otherwise (<c>no-introduction</c>, <c>not-caught</c>), or null.
/// </summary>
public sealed record DigestSending(string Mode, string Setting, string? Lock);

/// <summary>Summaries to MPs (design 8.4, 8.7, 8.9, 8.19): made by the rules; sent only as design 8.19 says.</summary>
public interface IDigests
{
    /// <summary>
    /// Expires the waiting summaries whose time has passed, then makes the weekly and before-vote summaries that are due at
    /// <paramref name="now"/>; returns those made.
    /// </summary>
    Task<IReadOnlyList<DigestRunInfo>> RunDueAsync(DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>One urgent summary for a record, approved by a moderator; may exceed the weekly ceiling by one; sent at once while sending is on.</summary>
    Task<Lt2.SharedKernel.Result<DigestRunInfo>> UrgentAsync(Guid recordId, string statement, StaffActor moderator, CancellationToken cancellationToken);

    /// <summary>Sending as it is now, with its locks (design 8.19.1).</summary>
    Task<DigestSending> SendingAsync(CancellationToken cancellationToken);

    /// <summary>
    /// "Siųsti" (design 8.19.2): lets a waiting summary go. Recipients are worked out now; under the summaries' lock, so two
    /// moderators pressing at once send it once (<c>digest.not-waiting</c> for the second). <c>digest.expired</c>,
    /// <c>digest.sending-off</c>, <c>digest.not-found</c>.
    /// </summary>
    Task<Lt2.SharedKernel.Result<DigestRunInfo>> SendAsync(Guid runId, StaffActor moderator, CancellationToken cancellationToken);

    /// <summary>"Nesiųsti" (design 8.19.2): declines a waiting summary with a statement of at least 20 characters, shown on its copy.</summary>
    Task<Lt2.SharedKernel.Result<DigestRunInfo>> CancelAsync(Guid runId, string statement, StaffActor moderator, CancellationToken cancellationToken);

    /// <summary>Waiting summaries whose time has passed become expired (design 8.19.2); returns how many.</summary>
    Task<int> ExpireDueAsync(DateTimeOffset now, CancellationToken cancellationToken);

    Task<IReadOnlyList<DigestRunInfo>> ListAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<DigestRunInfo>> OnDateAsync(DateOnly date, CancellationToken cancellationToken);

    /// <summary>The summaries that held one record, newest first; each run carries only that record's frozen item.</summary>
    Task<IReadOnlyList<DigestRunInfo>> ForRecordAsync(Guid recordId, CancellationToken cancellationToken);

    Task<Lt2.SharedKernel.Result<DigestPreview>> PreviewAsync(Guid runId, string mpSlug, CancellationToken cancellationToken);

    /// <summary>
    /// The MPs of a run and what happened, or would happen, for each MP's own address (design 8.19.3 statuses); staff only.
    /// Never the address, and nothing about an extra recipient. By last name, the names in the nominative.
    /// </summary>
    Task<IReadOnlyList<DigestMpInfo>> MpsAsync(Guid runId, CancellationToken cancellationToken);
}

/// <summary>An MP of a run, by name, and what happened for the MP's own address (design 8.19.3); staff only.</summary>
public sealed record DigestMpInfo(string Slug, string FirstName, string LastName, string Status);

/// <summary>An MP's choice of summaries as the MP's own page shows it (design 8.19.4). Shown to nobody else.</summary>
/// <param name="Frequency"><c>default</c>, <c>weekly</c>, <c>before-vote</c> or <c>stopped</c>.</param>
/// <param name="ExtraAddress">The extra recipient as written, confirmed or waiting for the official address's confirmation.</param>
public sealed record MpMailChoice(string Slug, string FirstName, string LastName, string Frequency, string? ExtraAddress, bool ExtraConfirmed);

/// <summary>
/// The MP's own summary preferences without an account (design 8.19.4): every call takes the link's token, answers the same
/// (<c>mp-mail.not-found</c>) for an unknown and a wrong token, and confirms every change by email to the official address.
/// Nothing here is logged with a token, an address or a choice.
/// </summary>
public interface IMpMail
{
    Task<Lt2.SharedKernel.Result<MpMailChoice>> GetAsync(string token, CancellationToken cancellationToken);

    /// <summary>Sets the frequency; <c>mp-mail.frequency-invalid</c>.</summary>
    Task<Lt2.SharedKernel.Result<MpMailChoice>> SetFrequencyAsync(string token, string frequency, CancellationToken cancellationToken);

    /// <summary>The one-click stop (RFC 8058): "Nesiųsti", and nothing else.</summary>
    Task<Lt2.SharedKernel.Result<Lt2.SharedKernel.Unit>> StopAsync(string token, CancellationToken cancellationToken);

    /// <summary>"Atsiųsti naują nuorodą": the old token stops working and a new link goes to the official address.</summary>
    Task<Lt2.SharedKernel.Result<Lt2.SharedKernel.Unit>> RenewAsync(string token, CancellationToken cancellationToken);

    /// <summary>Asks for an extra recipient: a confirmation link (24 hours, one use) goes to the official address. <c>mp-mail.address-invalid</c>.</summary>
    Task<Lt2.SharedKernel.Result<MpMailChoice>> AskExtraAsync(string token, string address, CancellationToken cancellationToken);

    /// <summary>Removes the extra recipient; the address is deleted.</summary>
    Task<Lt2.SharedKernel.Result<MpMailChoice>> RemoveExtraAsync(string token, CancellationToken cancellationToken);

    /// <summary>The official address confirms the extra recipient (the link's token); copies go from the next summary.</summary>
    Task<Lt2.SharedKernel.Result<MpMailChoice>> ConfirmExtraAsync(string confirmToken, CancellationToken cancellationToken);

    /// <summary>"Nebegauti kopijų" from a copy: the extra address is removed.</summary>
    Task<Lt2.SharedKernel.Result<Lt2.SharedKernel.Unit>> StopCopiesAsync(string stopToken, CancellationToken cancellationToken);

    /// <summary>The same choice for a linked account (design 8.19.4), by the MP's person number; the host checked the link.</summary>
    Task<Lt2.SharedKernel.Result<MpMailChoice>> ForPersonAsync(long seimasPersonId, CancellationToken cancellationToken);

    /// <summary>Sets the frequency for a linked account in a passkey session; confirmed by email to the official address too.</summary>
    Task<Lt2.SharedKernel.Result<MpMailChoice>> SetForPersonAsync(long seimasPersonId, string frequency, CancellationToken cancellationToken);
}

/// <summary>One version of the introduction letter (design 8.19.5).</summary>
public sealed record IntroductionVersion(int Version, string Subject, string Text, string SignerName, string SignerRole, DateTimeOffset WrittenAt, DateTimeOffset? SentAt);

/// <summary>
/// The administrator's view of the introduction letter: the version being written or last sent, the versions already sent,
/// how many sitting MPs would get it now (no letter yet, not suppressed, not stopped), and whether MP mail may leave here.
/// </summary>
public sealed record IntroductionState(IntroductionVersion? Current, IReadOnlyList<IntroductionVersion> Sent, int WouldGoTo, bool MailMayLeave);

/// <summary>The letter as an administrator writes it: subject, plain paragraphs, the signer's name and role.</summary>
public sealed record IntroductionDraft(string Subject, string Text, string SignerName, string SignerRole);

/// <summary>A sitting MP whose official address is suppressed (design 8.19.6), with the entry's reason and date. Staff only.</summary>
public sealed record SuppressedMp(Guid EntryId, string Slug, string FirstName, string LastName, string Reason, DateTimeOffset Since);

/// <summary>The suppression list as the screen "Nesiunčiama" shows it: the sitting MPs on it, and how many other entries.</summary>
public sealed record SuppressionView(IReadOnlyList<SuppressedMp> Mps, int Others);

/// <summary>
/// What administrators do with MP mail (design 8.19.5, 8.19.6): the introduction letter (write, test copy, send once per MP) and
/// the suppression list. Each change is audited without an address.
/// </summary>
public interface IMpMailAdministration
{
    Task<IntroductionState> IntroductionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Saves the letter: a version never sent is changed in place; once a version has been sent, a changed text is a new version.
    /// <c>introduction.invalid</c>.
    /// </summary>
    Task<Lt2.SharedKernel.Result<IntroductionVersion>> SaveIntroductionAsync(IntroductionDraft draft, StaffActor administrator, CancellationToken cancellationToken);

    /// <summary>A test copy of the current version to the administrator's own address; <c>introduction.none</c>.</summary>
    Task<Lt2.SharedKernel.Result<Lt2.SharedKernel.Unit>> SendIntroductionTestAsync(string ownAddress, StaffActor administrator, CancellationToken cancellationToken);

    /// <summary>
    /// Sends the current version to every sitting MP with an official address who has had none and is not suppressed; returns
    /// how many. <c>introduction.none</c>, <c>mp-mail.not-caught</c> (invented data, mail not caught).
    /// </summary>
    Task<Lt2.SharedKernel.Result<int>> SendIntroductionAsync(StaffActor administrator, CancellationToken cancellationToken);

    /// <summary>The versions sent, newest first, as the public page shows them (design 8.19.5).</summary>
    Task<IReadOnlyList<IntroductionVersion>> SentIntroductionsAsync(CancellationToken cancellationToken);

    Task<SuppressionView> SuppressionAsync(CancellationToken cancellationToken);

    /// <summary>Adds an address by hand (stored only hashed), with a statement; <c>mp-mail.address-invalid</c>, <c>suppression.exists</c>.</summary>
    Task<Lt2.SharedKernel.Result<Lt2.SharedKernel.Unit>> SuppressAsync(string address, string statement, StaffActor administrator, CancellationToken cancellationToken);

    /// <summary>Removes an entry, by its id or by the address typed, with a statement; <c>suppression.not-found</c>.</summary>
    Task<Lt2.SharedKernel.Result<Lt2.SharedKernel.Unit>> UnsuppressAsync(Guid? entryId, string? address, string statement, StaffActor administrator, CancellationToken cancellationToken);
}

/// <summary>
/// A bill the person follows, with its planned date or its last stage result, and when the person last opened its page (design
/// 8.11.8: from the follow's own moment).
/// </summary>
public sealed record FollowedBill(Lt2.SharedKernel.BillFacts Bill, Lt2.SharedKernel.PlannedDate? Planned, Lt2.SharedKernel.StageVoteFacts? LastStageResult, DateTimeOffset LastSeenAt);

/// <summary>The person's own follows (design 8.11.2, 8.14.14); there is no limit on how many.</summary>
public sealed record MyFollows(
    IReadOnlyList<FollowedBill> Bills,
    IReadOnlyList<Lt2.SharedKernel.MpFacts> Mps,
    IReadOnlyList<Lt2.SharedKernel.OrganisationFacts> Organisations,
    IReadOnlyList<FollowedMember>? Members = null);

/// <summary>A member with a public profile the person follows (design 8.25.5): the public id and the display name.</summary>
public sealed record FollowedMember(string ProfileId, string DisplayName);

/// <summary>
/// Which follow emails are on: planned and voted bills (design 8.11.2), organisations' statements (design 8.14.14), MPs' responses
/// on followed bills (design 8.20.1), followed members' arguments (design 8.25.5), comments under the person's own arguments
/// (design 8.23.9).
/// </summary>
public sealed record NoticeSettings(bool Planned, bool Voted, bool Statements = true, bool Responses = true, bool Members = true, bool Comments = true);

/// <summary>What one run of the follow job did: events recorded, events sent, emails written to the outbox.</summary>
public sealed record FollowRunOutcome(int Recorded, int Sent, int Emails);

/// <summary>
/// Following bills, MPs (design 8.11.2) and organisations (design 8.14.14). Every operation is the person's own: nothing here answers who follows what,
/// and nothing is combined with sides or invitations.
/// </summary>
public interface IFollows
{
    Task<MyFollows> MineAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// The bills the person follows, in one read (design 8.7, the front page), each with when the person last opened its page
    /// (design 8.11.8); their own only.
    /// </summary>
    Task<IReadOnlyDictionary<string, DateTimeOffset>> FollowedBillsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// The person opened a bill's page (design 8.11.8): on a bill they follow, keeps now as the moment of their last visit and
    /// answers the one before (at first the follow's own moment); on a bill they do not follow, stores nothing and answers null.
    /// </summary>
    Task<DateTimeOffset?> SeenBillAsync(Guid userId, string number, CancellationToken cancellationToken);

    /// <summary>
    /// Follows a bill: <c>bill.not-found</c>, <c>member.required</c>; following again changes nothing. Also used by the host
    /// when a person takes a first side on the bill (design 8.11.2); the follow holds nothing about the side.
    /// </summary>
    Task<Lt2.SharedKernel.Result<Lt2.SharedKernel.Unit>> FollowBillAsync(Guid userId, string number, CancellationToken cancellationToken);


    Task<Lt2.SharedKernel.Result<Lt2.SharedKernel.Unit>> UnfollowBillAsync(Guid userId, string number, CancellationToken cancellationToken);

    /// <summary>Follows an MP by profile slug: <c>mp.not-found</c>, <c>member.required</c>.</summary>
    Task<Lt2.SharedKernel.Result<Lt2.SharedKernel.Unit>> FollowMpAsync(Guid userId, string slug, CancellationToken cancellationToken);

    Task<Lt2.SharedKernel.Result<Lt2.SharedKernel.Unit>> UnfollowMpAsync(Guid userId, string slug, CancellationToken cancellationToken);

    /// <summary>
    /// Follows an active organisation by its profile slug (design 8.14.14, stage B): <c>organisation.not-found</c> (unknown,
    /// blocked or closed), <c>member.required</c>.
    /// </summary>
    Task<Lt2.SharedKernel.Result<Lt2.SharedKernel.Unit>> FollowOrganisationAsync(Guid userId, string slug, CancellationToken cancellationToken);

    Task<Lt2.SharedKernel.Result<Lt2.SharedKernel.Unit>> UnfollowOrganisationAsync(Guid userId, string slug, CancellationToken cancellationToken);

    /// <summary>
    /// Follows a member's public profile by its id (design 8.25.5): <c>member.not-found</c> for an id that is not public now
    /// (the same answer whether it never existed, was switched off or is the person's own), <c>member.required</c>.
    /// </summary>
    Task<Lt2.SharedKernel.Result<Lt2.SharedKernel.Unit>> FollowMemberAsync(Guid userId, string profileId, CancellationToken cancellationToken);

    Task<Lt2.SharedKernel.Result<Lt2.SharedKernel.Unit>> UnfollowMemberAsync(Guid userId, string profileId, CancellationToken cancellationToken);

    Task<NoticeSettings> NoticesAsync(Guid userId, CancellationToken cancellationToken);

    Task<Lt2.SharedKernel.Result<Lt2.SharedKernel.Unit>> SetNoticesAsync(Guid userId, NoticeSettings settings, CancellationToken cancellationToken);

    /// <summary>How often the person's emails come (design 8.24): <c>immediate</c> (also with no choice made), <c>daily</c> or <c>weekly</c>.</summary>
    Task<string> FrequencyAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Changes how often the person's emails come (design 8.24): <c>notices.frequency-invalid</c>, <c>member.required</c>.
    /// Audited without the choice; <paramref name="ipHash"/> is the request's hashed address.
    /// </summary>
    Task<Lt2.SharedKernel.Result<Lt2.SharedKernel.Unit>> SetFrequencyAsync(Guid userId, string frequency, string? ipHash, CancellationToken cancellationToken);

    /// <summary>
    /// The worker job (every 15 minutes): records new planned dates and stage votes of followed bills, and statements of
    /// followed organisations that became public, and emails the followers by the rules of design 8.11.2 and 8.14.14,
    /// through the outbox.
    /// </summary>
    Task<FollowRunOutcome> RunAsync(DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Daily housekeeping (design 8.24, the designer's proposal in section 12): deletes the follow job's records that no pass
    /// can still need: events first seen more than <c>FollowRules.EventRetention</c> ago (a planned date only once it is past,
    /// so a date still current is never recorded and told again), and the made daily and weekly passes as old. Returns the rows
    /// deleted.
    /// </summary>
    Task<int> PurgeEventsAsync(DateTimeOffset now, CancellationToken cancellationToken);
}