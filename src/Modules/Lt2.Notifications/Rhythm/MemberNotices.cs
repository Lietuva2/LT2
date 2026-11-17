using Lt2.Notifications.Follows;
using Lt2.Notifications.Infrastructure;
using Lt2.SharedKernel;

namespace Lt2.Notifications.Rhythm;

/// <summary>
/// One kind of member email that goes in the person's rhythm (design 8.24): the four follow emails (planned, voted, an
/// organisation's statement, MPs' answers), followed members' texts (8.25.5) and comments under one's arguments (8.23.9). A kind joins
/// by declaring itself, with no change to the follow job:
/// <list type="number">
/// <item>a class implementing this interface, registered in <see cref="NotificationsModule.AddNotificationsModule"/> with
/// <c>services.AddScoped&lt;IMemberNoticeKind, MyNotices&gt;()</c>;</item>
/// <item>its part of the gathered email, registered in <see cref="NotificationsModule.AddNotificationsDispatcher"/> with
/// <c>services.AddNoticePart&lt;MyEmail, MyTemplates&gt;(MessageKinds.MyEmail, order, t =&gt; t.MyPart)</c>, beside the
/// <c>AddEmail</c> of the email it is alone;</item>
/// <item>its switch on <see cref="NoticeSetting"/>, read in <see cref="TakeAsync"/> through <see cref="NoticePass.Tells"/>.</item>
/// </list>
/// The emails it never covers (codes, account and moderation notices, feedback, MP mail) do not go through here: they go at
/// once, each on its own, as they always did.
/// </summary>
internal interface IMemberNoticeKind
{
    /// <summary>
    /// Records what is new since the last run, once per event, with no person in the record (design 8.11.2), and returns how
    /// many events were recorded. Every kind records before any email of the run is made.
    /// </summary>
    Task<int> RecordAsync(DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// What this kind has for the people one pass serves (<see cref="NoticePass.Tells"/>): one notice per person and thing,
    /// each written as the email it would be alone. It marks the events it took for this pass in <see cref="NoticePass.Db"/>
    /// (the delivery saves them with the emails, in one transaction), so the next pass of the same kind does not take them again.
    /// </summary>
    Task<IReadOnlyList<DueNotice>> TakeAsync(NoticePass pass, CancellationToken cancellationToken);
}

/// <summary>
/// One thing a member is told (design 8.24): written for their address as the email it would be alone. When the run has more
/// for the same person, it becomes a line of the gathered email instead.
/// </summary>
/// <param name="UserId">The member; never written anywhere with the event.</param>
/// <param name="Seen">When the job first saw the event: it decides whose choice it belongs to.</param>
/// <param name="Write">The email alone, for the member's address.</param>
internal sealed record DueNotice(Guid UserId, DateTimeOffset Seen, Func<string, OutboxMessage> Write);

/// <summary>
/// One pass of a run of the follow job, as a kind sees it: which emails are being made, the moment, the cut of a gathered
/// email, and the people's switches and choices, read once for the whole run.
/// </summary>
internal sealed class NoticePass(
    Pass which, DateTimeOffset now, DateTimeOffset? cut, TimeOnly morningAt, NotificationsDb db, IReadOnlyDictionary<Guid, NoticeSetting> settings)
{
    private static readonly NoticeSetting Defaults = new();

    public Pass Which => which;

    public DateTimeOffset Now => now;

    /// <summary>A gathered email's moment (design 8.24): it holds what was first seen before it. Null for the other passes.</summary>
    public DateTimeOffset? Cut => cut;

    /// <summary><c>Follows:ResponsesAt</c>: the hour of the daily and weekly emails.</summary>
    public TimeOnly MorningAt => morningAt;

    /// <summary>The run's database context, in the delivery's transaction; a kind marks its events here.</summary>
    public NotificationsDb Db => db;

    /// <summary>Whether a gathered pass may take an event first seen at <paramref name="seen"/>; the other passes take any.</summary>
    public bool Takes(DateTimeOffset seen) => cut is not { } c || RhythmRules.InGathered(seen, c);

    /// <summary>The choice an event first seen at <paramref name="seen"/> belongs to for this person (<see cref="RhythmRules.ChoiceAt"/>).</summary>
    public string? ChoiceOf(Guid userId, DateTimeOffset seen) => RhythmRules.ChoiceAt(Choice(userId), seen);

    /// <summary>
    /// Whether this pass tells the person about an event first seen at <paramref name="seen"/>: the kind's switch is on, and the
    /// event belongs to a choice this pass serves.
    /// </summary>
    public bool Tells(Guid userId, DateTimeOffset seen, Func<NoticeSetting, bool> switchOn)
    {
        ArgumentNullException.ThrowIfNull(switchOn);
        return switchOn(settings.GetValueOrDefault(userId) ?? Defaults) && RhythmRules.Serves(which, ChoiceOf(userId, seen));
    }

    private RhythmChoice Choice(Guid userId) => settings.GetValueOrDefault(userId) is { } s
        ? new RhythmChoice(s.Frequency, s.FrequencySince, s.PreviousFrequency, s.PreviousSince)
        : RhythmChoice.Default;
}
