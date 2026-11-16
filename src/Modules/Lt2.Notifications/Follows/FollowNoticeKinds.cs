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
                        to, bill.Number, bill.Title, plan.Stage, plan.Date, FollowEvents.BillPath(bill.Number), NoticeDelivery.AccountPath))));
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
        var due = new List<DueNotice>();
        foreach (var e in events)
        {
            if (votes.GetValueOrDefault(e.Key) is not { } vote)
            {
                // Outside the look-back window by now: never sent rather than sent late.
                FollowEvents.Dropped(e, pass);
                continue;
            }

            var bill = facts.GetValueOrDefault(e.BillNumber) ?? new BillFacts(e.BillNumber, e.BillNumber);
            var told = followers[e.BillNumber]
                .Where(f => FollowRules.IsRecipient(f.CreatedAt, e.FirstSeen, vote.HeldAt) && pass.Tells(f.UserId, e.FirstSeen, n => n.Voted))
                .Select(f => f.UserId)
                .Distinct()
                .ToList();
            var choices = await FollowedMpChoicesAsync(pass.Db, told, vote, cancellationToken);
            var day = VilniusTime.DateOf(vote.HeldAt);
            var votePath = "/seimas/balsavimai/" + vote.VoteId.ToString(CultureInfo.InvariantCulture);
            foreach (var userId in told)
            {
                var theirs = choices(userId);
                due.Add(new DueNotice(userId, e.FirstSeen, to => OutboxMessage.Create(MessageKinds.FollowVotedEmail, new FollowVotedEmail(
                    to, bill.Number, bill.Title, vote.Stage, day, vote.IsConsensus, vote.For, vote.Against, vote.Abstain,
                    vote.OfficialNote, votePath, theirs, NoticeDelivery.AccountPath))));
            }

            FollowEvents.Taken(e, pass);
        }

        return due;
    }

    /// <summary>For a voted email: how each MP a person follows voted (public data), with every lookup made once.</summary>
    private async Task<Func<Guid, IReadOnlyList<FollowedMpChoice>>> FollowedMpChoicesAsync(
        NotificationsDb db, List<Guid> people, StageVoteFacts vote, CancellationToken ct)
    {
        if (people.Count == 0)
        {
            return _ => [];
        }

        var follows = await db.Follows.AsNoTracking().Where(f => people.Contains(f.UserId) && f.Kind == FollowKinds.Mp).ToListAsync(ct);
        var byPerson = follows.ToLookup(f => f.UserId, f => long.Parse(f.Key, CultureInfo.InvariantCulture));
        var all = byPerson.SelectMany(g => g).Distinct().ToList();
        if (all.Count == 0)
        {
            return _ => [];
        }

        var facts = await mps.MpsAsync(all, ct);
        var ballots = vote.HasResult ? await bills.ChoicesAsync(vote.VoteId, all, ct) : new Dictionary<long, string>();
        return person => byPerson[person]
            .Where(facts.ContainsKey)
            .Select(id => new FollowedMpChoice(facts[id].FirstName + " " + facts[id].LastName, vote.HasResult ? ballots.GetValueOrDefault(id, "did-not-vote") : null))
            .OrderBy(c => c.Name, LithuanianOrder)
            .ToList();
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
                        to, organisation.Name, organisation.ConfirmedBy, organisation.ConfirmedDomain, bill.Number, bill.Title, statement.Stance,
                        FollowEvents.BillPath(bill.Number), NoticeDelivery.AccountPath, day))));
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
                    to, bill.Number, bill.Title, day, listed, FollowEvents.BillPath(bill.Number) + "#atsakymai", NoticeDelivery.AccountPath))));
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
                        to, author.DisplayName, bill.Number, bill.Title,
                        FollowEvents.BillPath(bill.Number) + "#" + FollowRules.TextAnchor(argument.Kind, argument.Id),
                        "/nariai/" + author.ProfileId,
                        NoticeDelivery.AccountPath,
                        argument.Kind))));
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
        var shown = new Dictionary<string, PublicProfileFacts>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if ((await profiles.FindAsync(id, ct)).Profile is { } profile)
            {
                shown[id] = profile;
            }
        }

        return shown;
    }
}

/// <summary>
/// New comments under a member's own arguments (design 8.23.9; kind <c>comment</c>, one record per argument per run that found
/// some, key: <see cref="FollowRules.CommentKey"/>): the bill and the link to the argument, never a comment's text, its writer,
/// how many or "Sutinku". Each run reads the comments written since the run before that found some (design 8.23.14). The
/// author is not a follower: the argument's own author is told, by the switch <c>comments</c>, in their rhythm (design 8.24).
/// An argument whose comments in that window are all deleted or hidden by the time the email is made, or that is no longer
/// shown, is left out.
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
        var since = FollowRules.CommentsSince(last, now);
        if (since >= now)
        {
            return 0;
        }

        var commented = await comments.CommentedArgumentsAsync(since, now, cancellationToken);
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

        // Each run's window again, read now: what is still shown, and whose argument it is (the record holds no person).
        // A window never reaches further back than three days (FollowRules.CommentsSince), so older records only say "not the first run".
        var runs = events.Select(e => e.FirstSeen).Distinct().Order().ToList();
        var horizon = runs[0] - FollowRules.VoteSendWindow;
        var lastRun = runs[^1];
        var earlier = await pass.Db.BillEvents.AsNoTracking()
            .Where(e => e.Kind == BillEventKinds.Comment && e.FirstSeen >= horizon && e.FirstSeen < lastRun)
            .Select(e => e.FirstSeen)
            .Distinct()
            .ToListAsync(cancellationToken);
        var older = await pass.Db.BillEvents.AsNoTracking().AnyAsync(e => e.Kind == BillEventKinds.Comment && e.FirstSeen < horizon, cancellationToken);
        var still = new Dictionary<(string Number, string Key), CommentedArgument>();
        foreach (var run in runs)
        {
            var before = earlier.Where(t => t < run).Select(t => (DateTimeOffset?)t).DefaultIfEmpty(older ? horizon : null).Max();
            foreach (var argument in await comments.CommentedArgumentsAsync(FollowRules.CommentsSince(before, run), run, cancellationToken))
            {
                still[(argument.BillNumber, FollowRules.CommentKey(argument.ReasonId, run))] = argument;
            }
        }

        var facts = await bills.FindManyAsync(events.Select(e => e.BillNumber).Distinct().ToList(), cancellationToken);
        var due = new List<DueNotice>();
        foreach (var e in events)
        {
            if (still.GetValueOrDefault((e.BillNumber, e.Key)) is not { } argument)
            {
                FollowEvents.Dropped(e, pass);
                continue;
            }

            if (pass.Tells(argument.AuthorUserId, e.FirstSeen, n => n.Comments))
            {
                var bill = facts.GetValueOrDefault(e.BillNumber) ?? new BillFacts(e.BillNumber, e.BillNumber);
                var line = new CommentedArgumentLine(bill.Number, bill.Title, FollowEvents.BillPath(bill.Number) + "#reason-" + argument.ReasonId.ToString("D"));
                due.Add(new DueNotice(argument.AuthorUserId, e.FirstSeen, to => OutboxMessage.Create(MessageKinds.CommentNoticeEmail, new CommentNoticeEmail(
                    to, [line], NoticeDelivery.AccountPath))));
            }

            FollowEvents.Taken(e, pass);
        }

        return due;
    }
}