using System.Globalization;
using Lt2.Notifications.Infrastructure;
using Lt2.Notifications.Rhythm;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Notifications.Follows;

/// <summary>
/// What the follow kinds share: the events of one kind a pass takes (design 8.24), and the marks they get. Under "Iš karto"
/// an event is taken while its <see cref="BillEvent.Outcome"/> is waiting; a daily or weekly email takes, once, what was first
/// seen before its cut and that no email of its kind held; an event recorded and never to be sent (<c>skipped</c>) is in no email.
/// </summary>
internal static class FollowEvents
{
    public static async Task<List<BillEvent>> PendingAsync(NoticePass pass, string kind, CancellationToken ct)
    {
        var db = pass.Db;
        var query = db.BillEvents.Where(e => e.Kind == kind);
        query = pass.Which switch
        {
            Pass.Immediate => query.Where(e => e.Outcome == BillEventOutcomes.Waiting),
            Pass.Early => query.Where(e => e.EarlyAt == null && e.Outcome != BillEventOutcomes.Skipped),
            Pass.Daily => query.Where(e => e.DailyAt == null && e.Outcome != BillEventOutcomes.Skipped && e.FirstSeen < pass.Cut),
            Pass.Weekly => query.Where(e => e.WeeklyAt == null && e.Outcome != BillEventOutcomes.Skipped && e.FirstSeen < pass.Cut),
            _ => query.Where(_ => false),
        };
        return await query.OrderBy(e => e.FirstSeen).ToListAsync(ct);
    }

    /// <summary>The event was taken by this pass, whoever it went to (design 8.24: "once per event per person").</summary>
    public static void Taken(BillEvent e, NoticePass pass)
    {
        switch (pass.Which)
        {
            case Pass.Immediate:
                e.Outcome = BillEventOutcomes.Sent;
                e.SentAt = pass.Now;
                break;
            case Pass.Early:
                e.EarlyAt = pass.Now;
                break;
            case Pass.Daily:
                e.DailyAt = pass.Now;
                break;
            case Pass.Weekly:
                e.WeeklyAt = pass.Now;
                break;
        }
    }

    /// <summary>
    /// The event can no longer be told (no longer public, or out of the look-back): under "Iš karto" it is never sent; a gathered
    /// email leaves it out.
    /// </summary>
    public static void Dropped(BillEvent e, NoticePass pass)
    {
        if (pass.Which == Pass.Immediate)
        {
            e.Outcome = BillEventOutcomes.Skipped;
        }
        else
        {
            Taken(e, pass);
        }
    }

    public static BillEvent Record(NotificationsDb db, string number, string kind, string key, DateTimeOffset now, bool sendable)
    {
        var e = new BillEvent
        {
            BillNumber = number,
            Kind = kind,
            Key = key,
            FirstSeen = now,
            Outcome = sendable ? BillEventOutcomes.Waiting : BillEventOutcomes.Skipped,
        };
        db.BillEvents.Add(e);
        return e;
    }

    public static async Task<ILookup<string, Follow>> FollowersAsync(NotificationsDb db, string kind, IReadOnlyCollection<string> keys, CancellationToken ct) =>
        (await db.Follows.AsNoTracking().Where(f => f.Kind == kind && keys.Contains(f.Key)).ToListAsync(ct)).ToLookup(f => f.Key, StringComparer.Ordinal);

    public static string BillPath(string number) => "/seimas/" + Uri.EscapeDataString(number);
}

/// <summary>
/// A followed bill has a planned date (design 8.11.2, kind <c>planned</c>, key: the date). Under "Iš karto" at most one email a
/// bill a Vilnius day; for the daily and weekly choices the date current when the email is made, and at once, alone, when the
/// sitting comes before the person's next gathered email (design 8.24). A date already past, or no longer the bill's, is left out.
/// </summary>
internal sealed class PlannedNotices(IDbContextFactory<NotificationsDb> contexts, IBillCatalogue bills, IPlannedAgenda agenda) : IMemberNoticeKind
{
    public async Task<int> RecordAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var numbers = await db.Follows.AsNoTracking().Where(f => f.Kind == FollowKinds.Bill).Select(f => f.Key).Distinct().ToListAsync(cancellationToken);
        if (numbers.Count == 0)
        {
            return 0;
        }

        var planned = await agenda.ForBillsAsync(numbers, cancellationToken);
        var keys = planned.Values.Select(p => FollowRules.PlannedKey(p.Date)).Distinct().ToList();
        var known = (await db.BillEvents.AsNoTracking()
                .Where(e => e.Kind == BillEventKinds.Planned && numbers.Contains(e.BillNumber) && keys.Contains(e.Key))
                .Select(e => new { e.BillNumber, e.Key })
                .ToListAsync(cancellationToken))
            .Select(e => (e.BillNumber, e.Key))
            .ToHashSet();
        var recorded = 0;
        foreach (var (number, plan) in planned)
        {
            var key = FollowRules.PlannedKey(plan.Date);
            if (known.Add((number, key)))
            {
                FollowEvents.Record(db, number, BillEventKinds.Planned, key, now, FollowRules.PlannedIsSendable(plan.Date, now));
                recorded++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return recorded;
    }

    public async Task<IReadOnlyList<DueNotice>> TakeAsync(NoticePass pass, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pass);
        var events = await FollowEvents.PendingAsync(pass, BillEventKinds.Planned, cancellationToken);
        if (events.Count == 0)
        {
            return [];
        }

        var numbers = events.Select(e => e.BillNumber).Distinct().ToList();
        var followers = await FollowEvents.FollowersAsync(pass.Db, FollowKinds.Bill, numbers, cancellationToken);
        var planned = await agenda.ForBillsAsync(numbers, cancellationToken);
        var facts = await bills.FindManyAsync(numbers, cancellationToken);

        // Under "Iš karto": when each bill last had a "planned" email, for the limit of one a day (design 8.11.2).
        var lastSent = pass.Which != Pass.Immediate
            ? []
            : (await pass.Db.BillEvents.AsNoTracking()
                .Where(e => e.Kind == BillEventKinds.Planned && numbers.Contains(e.BillNumber) && e.SentAt != null)
                .GroupBy(e => e.BillNumber)
                .Select(g => new { Number = g.Key, Last = g.Max(e => e.SentAt) })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.Number, x => x.Last, StringComparer.Ordinal);

        var due = new List<DueNotice>();
        foreach (var e in events)
        {
            var date = FollowRules.PlannedDateOf(e.Key);
            var current = planned.GetValueOrDefault(e.BillNumber);
            if (!FollowRules.PlannedStillHolds(date, current?.Date, pass.Now))
            {
                // Moved or past: the current date is its own event, and a past one is told by the vote.
                if (pass.Which == Pass.Immediate)
                {
                    e.Outcome = BillEventOutcomes.Superseded;
                }
                else
                {
                    FollowEvents.Taken(e, pass);
                }

                continue;
            }

            if (pass.Which == Pass.Immediate && !FollowRules.PlannedMaySendNow(lastSent.GetValueOrDefault(e.BillNumber), pass.Now))
            {
                continue;
            }

            var bill = facts.GetValueOrDefault(e.BillNumber) ?? new BillFacts(e.BillNumber, e.BillNumber);
            var plan = current!;
            foreach (var f in followers[e.BillNumber])
            {
                if (!FollowRules.IsRecipient(f.CreatedAt, e.FirstSeen, null) || !pass.Tells(f.UserId, e.FirstSeen, n => n.Planned))
                {
                    continue;
                }

                var choice = pass.ChoiceOf(f.UserId, e.FirstSeen);
                var early = RhythmRules.CannotWait(date, choice, e.FirstSeen, pass.MorningAt);
                var tell = pass.Which switch
                {
                    Pass.Early => early,

                    // A gathered email leaves out what already went early (design 8.24: an event is in one email only).
                    Pass.Daily or Pass.Weekly => !(e.EarlyAt is not null && early),
                    _ => true,
                };
                if (tell)
                {
                    due.Add(new DueNotice(f.UserId, e.FirstSeen, to => OutboxMessage.Create(MessageKinds.FollowPlannedEmail, new FollowPlannedEmail(
                        to.To, bill.Number, bill.Title, plan.Stage, plan.Date, FollowEvents.BillPath(bill.Number), NoticeDelivery.AccountPath, to.StopToken))));
                }
            }

            FollowEvents.Taken(e, pass);
            if (pass.Which == Pass.Immediate)
            {
                lastSent[e.BillNumber] = pass.Now;
            }
        }

        return due;
    }
}

/// <summary>
/// The Seimas held a stage vote on a followed bill (design 8.11.2, kind <c>voted</c>, key: the vote): the result, the official
/// note and how each MP the person follows voted. Told to those who followed before the vote was held and before it was seen.
/// </summary>
internal sealed class VotedNotices(IDbContextFactory<NotificationsDb> contexts, IBillCatalogue bills, IMpDirectory mps) : IMemberNoticeKind
{
    private static readonly StringComparer LithuanianOrder = StringComparer.Create(CultureInfo.GetCultureInfo("lt-LT"), false);

    public async Task<int> RecordAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var numbers = await db.Follows.AsNoTracking().Where(f => f.Kind == FollowKinds.Bill).Select(f => f.Key).Distinct().ToListAsync(cancellationToken);
        if (numbers.Count == 0)
        {
            return 0;
        }

        var votes = await bills.StageResultsAsync(numbers, now - FollowRules.EventLookBack, cancellationToken);
        var keys = votes.Select(v => FollowRules.VotedKey(v.VoteId)).Distinct().ToList();
        var known = (await db.BillEvents.AsNoTracking()
                .Where(e => e.Kind == BillEventKinds.Voted && keys.Contains(e.Key))
                .Select(e => new { e.BillNumber, e.Key })
                .ToListAsync(cancellationToken))
            .Select(e => (e.BillNumber, e.Key))
            .ToHashSet();
        var recorded = 0;
        foreach (var vote in votes)
        {
            var key = FollowRules.VotedKey(vote.VoteId);
            if (known.Add((vote.BillNumber, key)))
            {
                FollowEvents.Record(db, vote.BillNumber, BillEventKinds.Voted, key, now, FollowRules.VoteIsSendable(vote.HeldAt, now));
                recorded++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return recorded;
    }

    public async Task<IReadOnlyList<DueNotice>> TakeAsync(NoticePass pass, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pass);
        if (pass.Which == Pass.Early)
        {
            return [];
        }

        var events = await FollowEvents.PendingAsync(pass, BillEventKinds.Voted, cancellationToken);
        if (events.Count == 0)
        {
            return [];
        }

        var numbers = events.Select(e => e.BillNumber).Distinct().ToList();
        var followers = await FollowEvents.FollowersAsync(pass.Db, FollowKinds.Bill, numbers, cancellationToken);
        var votes = (await bills.StageResultsAsync(numbers, pass.Now - FollowRules.EventLookBack, cancellationToken))
            .ToDictionary(v => FollowRules.VotedKey(v.VoteId), StringComparer.Ordinal);
        var facts = await bills.FindManyAsync(numbers, cancellationToken);
        var told = new List<(BillEvent Event, StageVoteFacts Vote, List<Guid> People)>();
        foreach (var e in events)
        {
            if (votes.GetValueOrDefault(e.Key) is not { } vote)
            {
                // Outside the look-back window by now: never sent rather than sent late.
                FollowEvents.Dropped(e, pass);
                continue;
            }

            told.Add((e, vote, followers[e.BillNumber]
                .Where(f => FollowRules.IsRecipient(f.CreatedAt, e.FirstSeen, vote.HeldAt) && pass.Tells(f.UserId, e.FirstSeen, n => n.Voted))
                .Select(f => f.UserId)
                .Distinct()
                .ToList()));
        }

        // The MPs each told person follows and their names: read once for the whole pass, not per vote.
        var people = told.SelectMany(t => t.People).Distinct().ToList();
        var followedMps = people.Count == 0
            ? Enumerable.Empty<(Guid UserId, long Mp)>().ToLookup(x => x.UserId, x => x.Mp)
            : (await pass.Db.Follows.AsNoTracking().Where(f => people.Contains(f.UserId) && f.Kind == FollowKinds.Mp).Select(f => new { f.UserId, f.Key }).ToListAsync(cancellationToken))
                .ToLookup(f => f.UserId, f => long.Parse(f.Key, CultureInfo.InvariantCulture));
        var allMps = followedMps.SelectMany(g => g).Distinct().ToList();
        var mpFacts = allMps.Count == 0 ? new Dictionary<long, MpFacts>() : await mps.MpsAsync(allMps, cancellationToken);

        var due = new List<DueNotice>();
        foreach (var (e, vote, toldPeople) in told)
        {
            var bill = facts.GetValueOrDefault(e.BillNumber) ?? new BillFacts(e.BillNumber, e.BillNumber);

            // One read per vote, of only the ballots of MPs someone told follows.
            var wanted = toldPeople.SelectMany(p => followedMps[p]).Distinct().Where(mpFacts.ContainsKey).ToList();
            var ballots = vote.HasResult && wanted.Count > 0 ? await bills.ChoicesAsync(vote.VoteId, wanted, cancellationToken) : new Dictionary<long, string>();
            var day = VilniusTime.DateOf(vote.HeldAt);
            var votePath = "/seimas/balsavimai/" + vote.VoteId.ToString(CultureInfo.InvariantCulture);
            foreach (var userId in toldPeople)
            {
                var theirs = followedMps[userId]
                    .Where(mpFacts.ContainsKey)
                    .Select(id => new FollowedMpChoice(mpFacts[id].FirstName + " " + mpFacts[id].LastName, vote.HasResult ? ballots.GetValueOrDefault(id, "did-not-vote") : null))
                    .OrderBy(c => c.Name, LithuanianOrder)
                    .ToList();
                due.Add(new DueNotice(userId, e.FirstSeen, to => OutboxMessage.Create(MessageKinds.FollowVotedEmail, new FollowVotedEmail(
                    to.To, bill.Number, bill.Title, vote.Stage, day, vote.IsConsensus, vote.For, vote.Against, vote.Abstain,
                    vote.OfficialNote, votePath, theirs, NoticeDelivery.AccountPath, to.StopToken))));
            }

            FollowEvents.Taken(e, pass);
        }

        return due;
    }
}
/// <summary>
/// A moderator closed a followed bill's record because the Seimas rejected the bill or its initiators withdrew it (design 8.1
/// step 6, 8.6; kind <c>closed</c>, key: <see cref="FollowRules.ClosedKey"/>): the counterpart of the voted email where the open
/// data has no vote, under the same switch ("voted") and rhythm. The bill, what happened, the day and the source on lrs.lt; never
/// the counts or anyone's side. Told to those who followed before the close; a close undone (reopened) by the time the email is
/// made is left out.
/// </summary>
internal sealed class ClosedNotices(IDbContextFactory<NotificationsDb> contexts, IBillCatalogue bills, IRecordClosureFeed closures) : IMemberNoticeKind
{
    public async Task<int> RecordAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var closed = await closures.ClosedAsync(now - FollowRules.EventLookBack, now, cancellationToken);
        if (closed.Count == 0)
        {
            return 0;
        }

        // An event is recorded only for a followed bill, as a vote's.
        var numbers = closed.Select(c => c.BillNumber).Distinct().ToList();
        var followed = (await db.Follows.AsNoTracking().Where(f => f.Kind == FollowKinds.Bill && numbers.Contains(f.Key)).Select(f => f.Key).Distinct().ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var fresh = closed.Where(c => followed.Contains(c.BillNumber)).ToList();
        var keys = fresh.Select(c => FollowRules.ClosedKey(c.ClosedAt)).ToList();
        var known = (await db.BillEvents.AsNoTracking().Where(e => e.Kind == BillEventKinds.Closed && keys.Contains(e.Key)).Select(e => new { e.BillNumber, e.Key }).ToListAsync(cancellationToken))
            .Select(e => (e.BillNumber, e.Key))
            .ToHashSet();
        var recorded = 0;
        foreach (var c in fresh)
        {
            var key = FollowRules.ClosedKey(c.ClosedAt);
            if (known.Add((c.BillNumber, key)))
            {
                FollowEvents.Record(db, c.BillNumber, BillEventKinds.Closed, key, now, FollowRules.ClosedIsSendable(c.ClosedAt, now));
                recorded++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return recorded;
    }

    public async Task<IReadOnlyList<DueNotice>> TakeAsync(NoticePass pass, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pass);
        if (pass.Which == Pass.Early)
        {
            return [];
        }

        var events = await FollowEvents.PendingAsync(pass, BillEventKinds.Closed, cancellationToken);
        if (events.Count == 0)
        {
            return [];
        }

        // What is closed now; a close undone meanwhile is not, and is never sent rather than sent late.
        var closedNow = (await closures.ClosedAsync(pass.Now - FollowRules.EventLookBack, pass.Now.AddSeconds(1), cancellationToken))
            .ToDictionary(c => (c.BillNumber, FollowRules.ClosedKey(c.ClosedAt)));
        var numbers = events.Select(e => e.BillNumber).Distinct().ToList();
        var followers = await FollowEvents.FollowersAsync(pass.Db, FollowKinds.Bill, numbers, cancellationToken);
        var facts = await bills.FindManyAsync(numbers, cancellationToken);
        var due = new List<DueNotice>();
        foreach (var e in events)
        {
            if (closedNow.GetValueOrDefault((e.BillNumber, e.Key)) is not { } closure)
            {
                FollowEvents.Dropped(e, pass);
                continue;
            }

            var bill = facts.GetValueOrDefault(e.BillNumber) ?? new BillFacts(e.BillNumber, e.BillNumber);
            foreach (var f in followers[e.BillNumber])
            {
                if (FollowRules.IsRecipient(f.CreatedAt, e.FirstSeen, closure.ClosedAt) && pass.Tells(f.UserId, e.FirstSeen, n => n.Voted))
                {
                    due.Add(new DueNotice(f.UserId, e.FirstSeen, to => OutboxMessage.Create(MessageKinds.FollowClosedEmail, new FollowClosedEmail(
                        to.To, bill.Number, bill.Title, closure.Reason, closure.DecidedOn, closure.SourceUrl, FollowEvents.BillPath(bill.Number),
                        NoticeDelivery.AccountPath, to.StopToken))));
                }
            }

            FollowEvents.Taken(e, pass);
        }

        return due;
    }
}

/// <summary>
/// A followed organisation's statement on a bill became public (design 8.14.14, stage B; kind <c>statement</c>, key: the
/// statement): the organisation with its label, the bill and the stance, never the text. Left out when it is no longer public or
/// its organisation is blocked or closed by the time the email is made.
/// </summary>
internal sealed class StatementNotices(IDbContextFactory<NotificationsDb> contexts, IBillCatalogue bills, IOrganisationDirectory organisations) : IMemberNoticeKind
{
    public async Task<int> RecordAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var ids = await FollowedOrganisationsAsync(db, cancellationToken);
        if (ids.Count == 0)
        {
            return 0;
        }

        var shown = await organisations.PublicStatementsAsync(ids, now - FollowRules.EventLookBack, cancellationToken);
        var keys = shown.Select(s => FollowRules.IdKey(s.Id)).ToList();
        var known = (await db.BillEvents.AsNoTracking().Where(e => e.Kind == BillEventKinds.Statement && keys.Contains(e.Key)).Select(e => e.Key).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var recorded = 0;
        foreach (var statement in shown)
        {
            var key = FollowRules.IdKey(statement.Id);
            if (known.Add(key))
            {
                FollowEvents.Record(db, statement.BillNumber, BillEventKinds.Statement, key, now, FollowRules.StatementIsSendable(statement.FirstPublicAt, now));
                recorded++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return recorded;
    }

    public async Task<IReadOnlyList<DueNotice>> TakeAsync(NoticePass pass, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pass);
        if (pass.Which == Pass.Early)
        {
            return [];
        }

        var events = await FollowEvents.PendingAsync(pass, BillEventKinds.Statement, cancellationToken);
        if (events.Count == 0)
        {
            return [];
        }

        var ids = await FollowedOrganisationsAsync(pass.Db, cancellationToken);
        var shown = (await organisations.PublicStatementsAsync(ids, pass.Now - FollowRules.EventLookBack, cancellationToken))
            .ToDictionary(s => FollowRules.IdKey(s.Id), StringComparer.Ordinal);
        var known = await organisations.FindManyAsync(ids, cancellationToken);
        var followers = await FollowEvents.FollowersAsync(pass.Db, FollowKinds.Organisation, ids.Select(FollowRules.IdKey).ToList(), cancellationToken);
        var facts = await bills.FindManyAsync(events.Select(e => e.BillNumber).Distinct().ToList(), cancellationToken);
        var due = new List<DueNotice>();
        foreach (var e in events)
        {
            if (shown.GetValueOrDefault(e.Key) is not { } statement
                || known.GetValueOrDefault(statement.OrganisationId) is not { Status: OrganisationStatus.Active } organisation)
            {
                // No longer public, or its organisation blocked or closed meanwhile: never sent rather than sent late.
                FollowEvents.Dropped(e, pass);
                continue;
            }

            var bill = facts.GetValueOrDefault(e.BillNumber) ?? new BillFacts(e.BillNumber, e.BillNumber);
            var day = VilniusTime.DateOf(statement.FirstPublicAt);
            foreach (var f in followers[FollowRules.IdKey(organisation.Id)])
            {
                if (FollowRules.IsStatementRecipient(f.CreatedAt, e.FirstSeen, statement.FirstPublicAt) && pass.Tells(f.UserId, e.FirstSeen, n => n.Statements))
                {
                    due.Add(new DueNotice(f.UserId, e.FirstSeen, to => OutboxMessage.Create(MessageKinds.FollowStatementEmail, new FollowStatementEmail(
                        to.To, organisation.Name, organisation.ConfirmedBy, organisation.ConfirmedDomain, bill.Number, bill.Title, statement.Stance,
                        FollowEvents.BillPath(bill.Number), NoticeDelivery.AccountPath, day, to.StopToken))));
                }
            }

            FollowEvents.Taken(e, pass);
        }

        return due;
    }

    private static async Task<List<Guid>> FollowedOrganisationsAsync(NotificationsDb db, CancellationToken ct) =>
        (await db.Follows.AsNoTracking().Where(f => f.Kind == FollowKinds.Organisation).Select(f => f.Key).Distinct().ToListAsync(ct))
            .Select(k => Guid.TryParse(k, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToList();
}

/// <summary>
/// MPs answered on a followed bill (design 8.20.1; kind <c>response</c>, one per answer, key: <see cref="FollowRules.ResponseKey"/>).
/// In one email, one part per bill: each MP's name, faction and stance, and the link to the bill's answers, never the
/// explanation. A person is told only about answers from after they followed, never about the MP they speak for; an answer
/// withdrawn, hidden or changed again by the time the email is made is left out (design 8.24).
/// </summary>
internal sealed class ResponseNotices(
    IDbContextFactory<NotificationsDb> contexts, IBillCatalogue bills, IMpDirectory mps, IMpResponseFeed responses, IMpLinks links) : IMemberNoticeKind
{
    private static readonly StringComparer LithuanianOrder = StringComparer.Create(CultureInfo.GetCultureInfo("lt-LT"), false);

    public async Task<int> RecordAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var announced = await responses.AnnouncedAsync(now - FollowRules.EventLookBack, now, cancellationToken);
        if (announced.Count == 0)
        {
            return 0;
        }

        // An event is recorded only for a followed bill (design 8.20.1).
        var numbers = announced.Select(r => r.BillNumber).Distinct().ToList();
        var followed = (await db.Follows.AsNoTracking().Where(f => f.Kind == FollowKinds.Bill && numbers.Contains(f.Key)).Select(f => f.Key).Distinct().ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var fresh = announced.Where(r => followed.Contains(r.BillNumber)).ToList();
        var keys = fresh.Select(r => FollowRules.ResponseKey(r.SeimasPersonId, r.AnnouncedAt)).ToList();
        var known = (await db.BillEvents.AsNoTracking().Where(e => e.Kind == BillEventKinds.Response && keys.Contains(e.Key)).Select(e => new { e.BillNumber, e.Key }).ToListAsync(cancellationToken))
            .Select(e => (e.BillNumber, e.Key))
            .ToHashSet();
        var recorded = 0;
        foreach (var r in fresh)
        {
            var key = FollowRules.ResponseKey(r.SeimasPersonId, r.AnnouncedAt);
            if (known.Add((r.BillNumber, key)))
            {
                FollowEvents.Record(db, r.BillNumber, BillEventKinds.Response, key, now, FollowRules.ResponseIsSendable(r.AnnouncedAt, now));
                recorded++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return recorded;
    }

    public async Task<IReadOnlyList<DueNotice>> TakeAsync(NoticePass pass, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pass);
        if (pass.Which == Pass.Early)
        {
            return [];
        }

        var events = await FollowEvents.PendingAsync(pass, BillEventKinds.Response, cancellationToken);
        if (events.Count == 0)
        {
            return [];
        }

        // What is public now, with the stance and the moment each event was recorded for; anything else is left out.
        var from = events.Min(e => FollowRules.ResponseOf(e.Key).AnnouncedAt);
        var publicNow = (await responses.AnnouncedAsync(from, pass.Now.AddSeconds(1), cancellationToken))
            .ToDictionary(r => (r.BillNumber, FollowRules.ResponseKey(r.SeimasPersonId, r.AnnouncedAt)));
        var numbers = events.Select(e => e.BillNumber).Distinct().ToList();
        var followers = await FollowEvents.FollowersAsync(pass.Db, FollowKinds.Bill, numbers, cancellationToken);
        var people = numbers.SelectMany(n => followers[n]).Select(f => f.UserId).Distinct().ToList();
        var speaksFor = people.Count == 0 ? new Dictionary<Guid, long>() : await links.SpeaksForAsync(people, cancellationToken);
        var persons = publicNow.Values.Select(r => r.SeimasPersonId).Distinct().ToList();
        var mpsOf = await mps.MpsAsync(persons, cancellationToken);
        var factions = await mps.FactionsAsync(persons, cancellationToken);
        var facts = await bills.FindManyAsync(numbers, cancellationToken);
        var due = new List<DueNotice>();
        foreach (var onBill in events.GroupBy(e => e.BillNumber))
        {
            var shown = new List<(BillEvent Event, PublicResponseFacts Response)>();
            foreach (var e in onBill)
            {
                if (publicNow.GetValueOrDefault((e.BillNumber, e.Key)) is { } response)
                {
                    shown.Add((e, response));
                    FollowEvents.Taken(e, pass);
                }
                else
                {
                    FollowEvents.Dropped(e, pass);
                }
            }

            if (shown.Count == 0)
            {
                continue;
            }

            var bill = facts.GetValueOrDefault(onBill.Key) ?? new BillFacts(onBill.Key, onBill.Key);
            foreach (var f in followers[onBill.Key])
            {
                var mine = shown
                    .Where(s => FollowRules.IsRecipient(f.CreatedAt, s.Event.FirstSeen, null) && pass.Tells(f.UserId, s.Event.FirstSeen, n => n.Responses))
                    .ToList();
                var told = FollowRules.ResponsesFor(f.CreatedAt, speaksFor.TryGetValue(f.UserId, out var mp) ? mp : null, mine.Select(s => s.Response));
                if (told.Count == 0)
                {
                    continue;
                }

                var day = VilniusTime.DateOf(told.Max(r => r.AnnouncedAt));
                var listed = Listed(told, mpsOf, factions);
                due.Add(new DueNotice(f.UserId, mine.Min(s => s.Event.FirstSeen), to => OutboxMessage.Create(MessageKinds.FollowResponsesEmail, new FollowResponsesEmail(
                    to.To, bill.Number, bill.Title, day, listed, FollowEvents.BillPath(bill.Number) + "#atsakymai", NoticeDelivery.AccountPath, to.StopToken))));
            }
        }

        return due;
    }

    /// <summary>The answers as the email lists them: by stance ("už", "prieš", "neapsisprendė"), then by last name, as on the bill's page.</summary>
    private static List<FollowedResponse> Listed(
        IEnumerable<PublicResponseFacts> told, IReadOnlyDictionary<long, MpFacts> mpsOf, IReadOnlyDictionary<long, string> factions) =>
        told.Select(r => (Response: r, Mp: mpsOf.GetValueOrDefault(r.SeimasPersonId)))
            .OrderBy(x => x.Response.Stance switch { "for" => 0, "against" => 1, _ => 2 })
            .ThenBy(x => x.Mp?.LastName ?? "", LithuanianOrder)
            .ThenBy(x => x.Mp?.FirstName ?? "", LithuanianOrder)
            .Select(x => new FollowedResponse(
                x.Mp is { } mp ? mp.FirstName + " " + mp.LastName : x.Response.SeimasPersonId.ToString(CultureInfo.InvariantCulture),
                factions.GetValueOrDefault(x.Response.SeimasPersonId),
                x.Response.Stance))
            .ToList();
}

/// <summary>
/// A followed member published an argument or a comment in their own voice (design 8.25.5; kind <c>member</c>, key: the text's
/// id, under its bill): the member's display name, the bill and the links to the text and to the member's page, never the text,
/// a side or a count. Told to those who followed the member before it became public and before the job saw it, never to its
/// author; left out when by the time the email is made the text is no longer shown or the member's profile is switched off.
/// </summary>
internal sealed class MemberNotices(
    IDbContextFactory<NotificationsDb> contexts, IBillCatalogue bills, IPublicProfiles profiles, IMemberTexts texts) : IMemberNoticeKind
{
    public async Task<int> RecordAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var shown = await FollowedProfilesAsync(db, cancellationToken);
        if (shown.Count == 0)
        {
            return 0;
        }

        var published = await texts.PublishedSinceAsync(shown.Values.Select(p => p.UserId).ToList(), now - FollowRules.EventLookBack, cancellationToken);
        var keys = published.Select(a => FollowRules.IdKey(a.Id)).ToList();
        var known = (await db.BillEvents.AsNoTracking().Where(e => e.Kind == BillEventKinds.Member && keys.Contains(e.Key)).Select(e => e.Key).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var recorded = 0;
        foreach (var argument in published)
        {
            var key = FollowRules.IdKey(argument.Id);
            if (known.Add(key))
            {
                FollowEvents.Record(db, argument.BillNumber, BillEventKinds.Member, key, now, FollowRules.MemberArgumentIsSendable(argument.PublishedAt, now));
                recorded++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return recorded;
    }

    public async Task<IReadOnlyList<DueNotice>> TakeAsync(NoticePass pass, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pass);
        if (pass.Which == Pass.Early)
        {
            return [];
        }

        var events = await FollowEvents.PendingAsync(pass, BillEventKinds.Member, cancellationToken);
        if (events.Count == 0)
        {
            return [];
        }

        var shown = await FollowedProfilesAsync(pass.Db, cancellationToken);
        var byAuthor = shown.Values.ToDictionary(p => p.UserId);
        var published = (await texts.PublishedSinceAsync(byAuthor.Keys.ToList(), pass.Now - FollowRules.EventLookBack, cancellationToken))
            .ToDictionary(a => FollowRules.IdKey(a.Id), StringComparer.Ordinal);
        var followers = await FollowEvents.FollowersAsync(pass.Db, FollowKinds.Member, shown.Keys.ToList(), cancellationToken);
        var facts = await bills.FindManyAsync(events.Select(e => e.BillNumber).Distinct().ToList(), cancellationToken);
        var due = new List<DueNotice>();
        foreach (var e in events)
        {
            if (published.GetValueOrDefault(e.Key) is not { } argument || byAuthor.GetValueOrDefault(argument.AuthorUserId) is not { } author)
            {
                // No longer public, or the profile switched off meanwhile: never sent rather than sent late.
                FollowEvents.Dropped(e, pass);
                continue;
            }

            var bill = facts.GetValueOrDefault(e.BillNumber) ?? new BillFacts(e.BillNumber, e.BillNumber);
            foreach (var f in followers[author.ProfileId])
            {
                if (FollowRules.IsMemberArgumentRecipient(f.UserId, author.UserId, f.CreatedAt, e.FirstSeen, argument.PublishedAt) && pass.Tells(f.UserId, e.FirstSeen, n => n.Members))
                {
                    due.Add(new DueNotice(f.UserId, e.FirstSeen, to => OutboxMessage.Create(MessageKinds.FollowMemberEmail, new FollowMemberEmail(
                        to.To, author.DisplayName, bill.Number, bill.Title,
                        FollowRules.TextPath(bill.Number, argument.Kind, argument.Id),
                        MemberAddress.Page(author.ProfileId, author.DisplayName),
                        NoticeDelivery.AccountPath,
                        argument.Kind,
                        VilniusTime.DateOf(argument.PublishedAt),
                        to.StopToken))));
                }
            }

            FollowEvents.Taken(e, pass);
        }

        return due;
    }

    /// <summary>The followed members whose profile is public now, by profile id.</summary>
    private async Task<Dictionary<string, PublicProfileFacts>> FollowedProfilesAsync(NotificationsDb db, CancellationToken ct)
    {
        var ids = await db.Follows.AsNoTracking().Where(f => f.Kind == FollowKinds.Member).Select(f => f.Key).Distinct().ToListAsync(ct);
        return new Dictionary<string, PublicProfileFacts>(await profiles.PublicByIdsAsync(ids, ct), StringComparer.Ordinal);
    }
}

/// <summary>
/// New comments under a member's own arguments (design 8.23.9; kind <c>comment</c>, one record per argument per run that found
/// some, key: <see cref="FollowRules.CommentKey"/>): the bill and the link to the argument, never a comment's text, its writer,
/// how many or "Sutinku". Each run reads the comments of its window (<see cref="FollowRules.CommentsWindow"/>). The author is
/// not a follower: the argument's own author is told, by the switch <c>comments</c>, in their rhythm (design 8.24). An email
/// holds one line per argument however many runs found comments on it, with the day of the latest; an argument whose
/// comments in those windows are all deleted or hidden by the time the email is made, or that is no longer shown, is left out.
/// </summary>
internal sealed class CommentNotices(IDbContextFactory<NotificationsDb> contexts, IBillCatalogue bills, ICommentFeed comments) : IMemberNoticeKind
{
    public async Task<int> RecordAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Under one lock, so two runs at once read one window and record it once.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended({0}, 0))", ["lt2.follows.comments"], cancellationToken);
        var last = await db.BillEvents.AsNoTracking().Where(e => e.Kind == BillEventKinds.Comment).MaxAsync(e => (DateTimeOffset?)e.FirstSeen, cancellationToken);
        if (last >= now)
        {
            return 0;
        }

        var (since, until) = FollowRules.CommentsWindow(last, now);
        var commented = since >= until ? [] : await comments.CommentedArgumentsAsync(since, until, cancellationToken);
        foreach (var argument in commented)
        {
            FollowEvents.Record(db, argument.BillNumber, BillEventKinds.Comment, FollowRules.CommentKey(argument.ReasonId, now), now, sendable: true);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return commented.Count;
    }

    public async Task<IReadOnlyList<DueNotice>> TakeAsync(NoticePass pass, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pass);
        if (pass.Which == Pass.Early)
        {
            return [];
        }

        var events = await FollowEvents.PendingAsync(pass, BillEventKinds.Comment, cancellationToken);
        if (events.Count == 0)
        {
            return [];
        }

        // Each run's window again (the record holds no person), from one read of the comments of all of them: what is still
        // shown, and whose argument it is. A window never reaches further back than three days, so older records only say
        // "not the first run".
        var runs = events.Select(e => e.FirstSeen).Distinct().Order().ToList();
        var horizon = runs[0] - FollowRules.VoteSendWindow;
        var lastRun = runs[^1];
        var earlier = await pass.Db.BillEvents.AsNoTracking()
            .Where(e => e.Kind == BillEventKinds.Comment && e.FirstSeen >= horizon && e.FirstSeen < lastRun)
            .Select(e => e.FirstSeen)
            .Distinct()
            .ToListAsync(cancellationToken);
        var older = await pass.Db.BillEvents.AsNoTracking().AnyAsync(e => e.Kind == BillEventKinds.Comment && e.FirstSeen < horizon, cancellationToken);
        var windows = runs.ToDictionary(r => r, r => FollowRules.CommentsWindow(earlier.Where(t => t < r).Select(t => (DateTimeOffset?)t).DefaultIfEmpty(older ? horizon : null).Max(), r));
        var times = await comments.CommentTimesAsync(windows.Values.Min(w => w.Since), windows.Values.Max(w => w.Until), cancellationToken);
        var still = new Dictionary<(string Number, string Key), CommentedArgumentAt>();
        foreach (var (run, (since, until)) in windows)
        {
            // The earliest comment of each run's window under each argument: the email links to the first new one (8.23.14).
            foreach (var comment in times.Where(c => c.At >= since && c.At < until).OrderByDescending(c => c.At).ThenByDescending(c => c.CommentId))
            {
                still[(comment.BillNumber, FollowRules.CommentKey(comment.ReasonId, run))] = comment;
            }
        }

        // One line per argument and person in this pass, whatever the number of runs that found comments on it.
        var told = new Dictionary<(Guid Author, Guid Reason), (CommentedArgumentAt Argument, DateTimeOffset FirstSeen, DateTimeOffset LastSeen)>();
        foreach (var e in events)
        {
            if (still.GetValueOrDefault((e.BillNumber, e.Key)) is not { } argument)
            {
                FollowEvents.Dropped(e, pass);
                continue;
            }

            var key = (argument.AuthorUserId, argument.ReasonId);
            told[key] = told.TryGetValue(key, out var seen)
                ? (argument.At < seen.Argument.At ? argument : seen.Argument, seen.FirstSeen < e.FirstSeen ? seen.FirstSeen : e.FirstSeen, seen.LastSeen > e.FirstSeen ? seen.LastSeen : e.FirstSeen)
                : (argument, e.FirstSeen, e.FirstSeen);
            FollowEvents.Taken(e, pass);
        }

        var facts = await bills.FindManyAsync(told.Values.Select(t => t.Argument.BillNumber).Distinct().ToList(), cancellationToken);
        var due = new List<DueNotice>();
        foreach (var (argument, firstSeen, lastSeen) in told.Values.OrderBy(t => t.Argument.BillNumber, StringComparer.Ordinal))
        {
            if (!pass.Tells(argument.AuthorUserId, firstSeen, n => n.Comments))
            {
                continue;
            }

            var bill = facts.GetValueOrDefault(argument.BillNumber) ?? new BillFacts(argument.BillNumber, argument.BillNumber);
            // The link lands in the discussion, on the first new comment the email reports (design 8.23.6, 8.23.14 point 4): the
            // argument shown, its "Aptarimas" open, that comment marked; one no longer shown leaves the page as usual.
            var line = new CommentedArgumentLine(bill.Number, bill.Title, BillTextLinks.ToComment(bill.Number, argument.CommentId), VilniusTime.DateOf(lastSeen));
            due.Add(new DueNotice(argument.AuthorUserId, firstSeen, to => OutboxMessage.Create(MessageKinds.CommentNoticeEmail, new CommentNoticeEmail(
                to.To, [line], NoticeDelivery.AccountPath, to.StopToken))));
        }

        return due;
    }
}