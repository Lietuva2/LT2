using System.Data.Common;
using System.Globalization;
using Lt2.Notifications.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Lt2.Notifications.Follows;

/// <summary>
/// Following bills and MPs (design 8.11.2) and organisations (design 8.14.14, stage B), and the follow emails. Reads the Seimas
/// through <see cref="IBillCatalogue"/>, <see cref="IPlannedAgenda"/> and <see cref="IMpDirectory"/>, organisations through
/// <see cref="IOrganisationDirectory"/>, people through <see cref="IParticipants"/>; never a side.
/// Logs carry counts only: no address, no person, no bill a person follows.
/// </summary>
internal sealed class FollowService(
    IDbContextFactory<NotificationsDb> contexts,
    IParticipants participants,
    IBillCatalogue bills,
    IPlannedAgenda agenda,
    IMpDirectory mps,
    IOrganisationDirectory organisations,
    ISettings settings,
    IOutbox outbox,
    TimeProvider clock,
    ILogger<FollowService> logger) : IFollows
{
    /// <summary>The account page, where every follow email links (design 8.11.2: no token link in this round).</summary>
    public const string AccountPath = "/sveiki#sekami";

    /// <summary>How far back the job looks for stage votes; older ones could only be recorded and skipped.</summary>
    private static readonly TimeSpan VoteLookBack = TimeSpan.FromDays(7);

    /// <summary>How far back the job looks for statements that became public; older ones could only be recorded and skipped.</summary>
    private static readonly TimeSpan StatementLookBack = TimeSpan.FromDays(7);

    private static readonly StringComparer LithuanianOrder = StringComparer.Create(CultureInfo.GetCultureInfo("lt-LT"), false);

    public async Task<MyFollows> MineAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var follows = await db.Follows.AsNoTracking().Where(f => f.UserId == userId).OrderBy(f => f.CreatedAt).ToListAsync(cancellationToken);
        var numbers = follows.Where(f => f.Kind == FollowKinds.Bill).Select(f => f.Key).ToList();
        var facts = numbers.Count == 0 ? new Dictionary<string, BillFacts>() : await bills.FindManyAsync(numbers, cancellationToken);
        var planned = numbers.Count == 0 ? new Dictionary<string, PlannedDate>() : await agenda.ForBillsAsync(numbers, cancellationToken);
        var votes = numbers.Count == 0 ? new Dictionary<string, StageVoteFacts>() : await bills.LatestStageResultsAsync(numbers, cancellationToken);
        var ids = MpIds(follows);
        var people = ids.Count == 0 ? new Dictionary<long, MpFacts>() : await mps.MpsAsync(ids, cancellationToken);
        var organisationIds = OrganisationIds(follows);
        var known = await organisations.FindManyAsync(organisationIds, cancellationToken);
        return new MyFollows(
            numbers.Select(n => new FollowedBill(
                    facts.GetValueOrDefault(n) ?? new BillFacts(n, n),
                    planned.GetValueOrDefault(n),
                    votes.GetValueOrDefault(n)))
                .ToList(),
            ids.Select(id => people.GetValueOrDefault(id)).OfType<MpFacts>().ToList(),
            await settings.GetAsync(SettingKeys.FollowsMaxPerAccount, cancellationToken),
            organisationIds.Select(id => known.GetValueOrDefault(id)).OfType<OrganisationFacts>().ToList());
    }

    public async Task<Result<Unit>> FollowBillAsync(Guid userId, string number, CancellationToken cancellationToken)
    {
        var bill = await bills.FindAsync(number.Trim().ToUpperInvariant(), cancellationToken);
        return bill is null ? Error.NotFound("bill.not-found") : await AddAsync(userId, FollowKinds.Bill, bill.Number, cancellationToken);
    }

    public async Task<Result<Unit>> UnfollowBillAsync(Guid userId, string number, CancellationToken cancellationToken) =>
        await RemoveAsync(userId, FollowKinds.Bill, number.Trim().ToUpperInvariant(), cancellationToken);

    public async Task<Result<Unit>> FollowMpAsync(Guid userId, string slug, CancellationToken cancellationToken)
    {
        var mp = await mps.FindMpAsync(slug, cancellationToken);
        return mp is null ? Error.NotFound("mp.not-found") : await AddAsync(userId, FollowKinds.Mp, FollowRules.MpKey(mp.SeimasPersonId), cancellationToken);
    }

    public async Task<Result<Unit>> UnfollowMpAsync(Guid userId, string slug, CancellationToken cancellationToken)
    {
        var mp = await mps.FindMpAsync(slug, cancellationToken);
        return mp is null ? Result.Ok() : await RemoveAsync(userId, FollowKinds.Mp, FollowRules.MpKey(mp.SeimasPersonId), cancellationToken);
    }

    public async Task<NoticeSettings> NoticesAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.NoticeSettings.AsNoTracking().FirstOrDefaultAsync(n => n.UserId == userId, cancellationToken);
        return new NoticeSettings(row?.Planned ?? true, row?.Voted ?? true, row?.Statements ?? true);
    }

    public async Task<Result<Unit>> SetNoticesAsync(Guid userId, NoticeSettings settings, CancellationToken cancellationToken)
    {
        if (await participants.FindAsync(userId, cancellationToken) is null)
        {
            return Error.Forbidden("member.required");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.NoticeSettings.FirstOrDefaultAsync(n => n.UserId == userId, cancellationToken);
        if (row is null)
        {
            row = new NoticeSetting { UserId = userId };
            db.NoticeSettings.Add(row);
        }

        row.Planned = settings.Planned;
        row.Voted = settings.Voted;
        row.Statements = settings.Statements;
        row.ChangedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<Unit>> FollowOrganisationAsync(Guid userId, string slug, CancellationToken cancellationToken)
    {
        // Only an active organisation can be followed: a blocked or closed one publishes nothing (design 8.14.14).
        var organisation = await organisations.FindAsync(slug, cancellationToken);
        return organisation is not { Status: OrganisationStatus.Active }
            ? Error.NotFound("organisation.not-found")
            : await AddAsync(userId, FollowKinds.Organisation, FollowRules.IdKey(organisation.Id), cancellationToken);
    }

    public async Task<Result<Unit>> UnfollowOrganisationAsync(Guid userId, string slug, CancellationToken cancellationToken)
    {
        var organisation = await organisations.FindAsync(slug, cancellationToken);
        return organisation is null ? Result.Ok() : await RemoveAsync(userId, FollowKinds.Organisation, FollowRules.IdKey(organisation.Id), cancellationToken);
    }

    public async Task<FollowRunOutcome> RunAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var bills = await RunBillsAsync(now, cancellationToken);
        var statements = await RunStatementsAsync(now, cancellationToken);
        var outcome = new FollowRunOutcome(bills.Recorded + statements.Recorded, bills.Sent + statements.Sent, bills.Emails + statements.Emails);
        logger.LogInformation("Follow job: {Recorded} events recorded, {Sent} sent, {Emails} emails", outcome.Recorded, outcome.Sent, outcome.Emails);
        return outcome;
    }

    private static List<long> MpIds(IEnumerable<Follow> follows) =>
        follows.Where(f => f.Kind == FollowKinds.Mp).Select(f => long.Parse(f.Key, CultureInfo.InvariantCulture)).ToList();

    private static List<Guid> OrganisationIds(IEnumerable<Follow> follows) =>
        follows.Where(f => f.Kind == FollowKinds.Organisation).Select(f => Guid.TryParse(f.Key, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).Distinct().ToList();

    private static BillEvent Record(NotificationsDb db, string number, string kind, string key, DateTimeOffset now, bool sendable)
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

    private async Task<Result<Unit>> AddAsync(Guid userId, string kind, string key, CancellationToken ct)
    {
        if (await participants.FindAsync(userId, ct) is null)
        {
            return Error.Forbidden("member.required");
        }

        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // One person's follows are counted under their own lock, so the limit holds under parallel requests.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended({0}, 0))", ["lt2.follows:" + userId.ToString("N")], ct);
        if (await db.Follows.AnyAsync(f => f.UserId == userId && f.Kind == kind && f.Key == key, ct))
        {
            return Result.Ok();
        }

        var count = await db.Follows.CountAsync(f => f.UserId == userId, ct);
        if (!FollowRules.MayFollowMore(count, await settings.GetAsync(SettingKeys.FollowsMaxPerAccount, ct)))
        {
            return Error.Conflict("follow.limit");
        }

        db.Follows.Add(new Follow { UserId = userId, Kind = kind, Key = key, CreatedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Ok();
    }

    private async Task<Result<Unit>> RemoveAsync(Guid userId, string kind, string key, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await db.Follows.Where(f => f.UserId == userId && f.Kind == kind && f.Key == key).ExecuteDeleteAsync(ct);
        return Result.Ok();
    }

    /// <summary>Planned dates and stage votes of followed bills (design 8.11.2).</summary>
    private async Task<FollowRunOutcome> RunBillsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var followersOf = (await db.Follows.AsNoTracking().Where(f => f.Kind == FollowKinds.Bill).ToListAsync(cancellationToken))
            .ToLookup(f => f.Key, StringComparer.Ordinal);
        var numbers = followersOf.Select(g => g.Key).ToList();
        if (numbers.Count == 0)
        {
            return new FollowRunOutcome(0, 0, 0);
        }

        var planned = await agenda.ForBillsAsync(numbers, cancellationToken);
        var votes = await bills.StageResultsAsync(numbers, now - VoteLookBack, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Only the bill's own events: an organisation's statement on it is the statements' part (RunStatementsAsync).
        var events = await db.BillEvents
            .Where(e => numbers.Contains(e.BillNumber) && (e.Kind == BillEventKinds.Planned || e.Kind == BillEventKinds.Voted))
            .ToListAsync(cancellationToken);

        // Record what is seen for the first time, once (design 8.11.2).
        var recorded = 0;
        foreach (var (number, plan) in planned)
        {
            var key = FollowRules.PlannedKey(plan.Date);
            if (!events.Any(e => e.BillNumber == number && e.Kind == BillEventKinds.Planned && e.Key == key))
            {
                events.Add(Record(db, number, BillEventKinds.Planned, key, now, FollowRules.PlannedIsSendable(plan.Date, now)));
                recorded++;
            }
        }

        foreach (var vote in votes)
        {
            var key = FollowRules.VotedKey(vote.VoteId);
            if (!events.Any(e => e.BillNumber == vote.BillNumber && e.Kind == BillEventKinds.Voted && e.Key == key))
            {
                events.Add(Record(db, vote.BillNumber, BillEventKinds.Voted, key, now, FollowRules.VoteIsSendable(vote.HeldAt, now)));
                recorded++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var sent = 0;
        var emails = 0;
        var facts = await bills.FindManyAsync(numbers, cancellationToken);
        foreach (var waiting in events.Where(e => e.Outcome == BillEventOutcomes.Waiting).OrderBy(e => e.FirstSeen).ToList())
        {
            var bill = facts.GetValueOrDefault(waiting.BillNumber) ?? new BillFacts(waiting.BillNumber, waiting.BillNumber);
            var followers = followersOf[waiting.BillNumber];
            var billPath = "/seimas/" + Uri.EscapeDataString(bill.Number);
            if (waiting.Kind == BillEventKinds.Planned)
            {
                var date = FollowRules.PlannedDateOf(waiting.Key);
                if (!FollowRules.PlannedStillHolds(date, planned.GetValueOrDefault(waiting.BillNumber)?.Date, now))
                {
                    waiting.Outcome = BillEventOutcomes.Superseded;
                    continue;
                }

                var lastSent = events.Where(e => e.BillNumber == waiting.BillNumber && e.Kind == BillEventKinds.Planned).Max(e => e.SentAt);
                if (!FollowRules.PlannedMaySendNow(lastSent, now))
                {
                    continue;
                }

                var plan = planned[waiting.BillNumber];
                emails += await SendAsync(
                    db, followers.Where(f => FollowRules.IsRecipient(f.CreatedAt, waiting.FirstSeen, null)), n => n.Planned,
                    (person, _) => OutboxMessage.Create(MessageKinds.FollowPlannedEmail, new FollowPlannedEmail(
                        person.Email, bill.Number, bill.Title, plan.Stage, plan.Date, billPath, AccountPath)),
                    transaction, cancellationToken);
            }
            else
            {
                var vote = votes.FirstOrDefault(v => FollowRules.VotedKey(v.VoteId) == waiting.Key);
                if (vote is null)
                {
                    // Outside the look-back window by now: never sent rather than sent late.
                    waiting.Outcome = BillEventOutcomes.Skipped;
                    continue;
                }

                var day = VilniusTime.DateOf(vote.HeldAt);
                var votePath = "/seimas/balsavimai/" + vote.VoteId.ToString(CultureInfo.InvariantCulture);
                emails += await SendAsync(
                    db, followers.Where(f => FollowRules.IsRecipient(f.CreatedAt, waiting.FirstSeen, vote.HeldAt)), n => n.Voted,
                    (person, choices) => OutboxMessage.Create(MessageKinds.FollowVotedEmail, new FollowVotedEmail(
                        person.Email, bill.Number, bill.Title, vote.Stage, day, vote.IsConsensus, vote.For, vote.Against, vote.Abstain,
                        vote.OfficialNote, votePath, choices, AccountPath)),
                    transaction, cancellationToken, vote);
            }

            waiting.Outcome = BillEventOutcomes.Sent;
            waiting.SentAt = now;
            sent++;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new FollowRunOutcome(recorded, sent, emails);
    }

    /// <summary>
    /// Statements of followed organisations that became public (design 8.14.14, stage B): each recorded once, under its bill,
    /// when the job first sees it; one email per person, to those who followed the organisation before it became public and
    /// before the job saw it; nothing older than three days when first seen; nothing for a change of a statement, which keeps
    /// its moment, or for a statement no longer public by the time it would be sent (hidden, waiting, withdrawn, or its
    /// organisation blocked or closed).
    /// </summary>
    private async Task<FollowRunOutcome> RunStatementsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var organisationFollows = await db.Follows.AsNoTracking().Where(f => f.Kind == FollowKinds.Organisation).ToListAsync(cancellationToken);
        var followersOf = organisationFollows.ToLookup(f => f.Key, StringComparer.Ordinal);
        var ids = OrganisationIds(organisationFollows);
        var waitingAlready = await db.BillEvents.AsNoTracking().AnyAsync(e => e.Kind == BillEventKinds.Statement && e.Outcome == BillEventOutcomes.Waiting, cancellationToken);
        if (ids.Count == 0 && !waitingAlready)
        {
            return new FollowRunOutcome(0, 0, 0);
        }

        var shown = await organisations.PublicStatementsAsync(ids, now - StatementLookBack, cancellationToken);
        var shownByKey = shown.ToDictionary(s => FollowRules.IdKey(s.Id), StringComparer.Ordinal);
        var keys = shownByKey.Keys.ToList();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var events = await db.BillEvents
            .Where(e => e.Kind == BillEventKinds.Statement && (keys.Contains(e.Key) || e.Outcome == BillEventOutcomes.Waiting))
            .ToListAsync(cancellationToken);

        var recorded = 0;
        foreach (var (key, statement) in shownByKey)
        {
            if (!events.Any(e => e.Key == key))
            {
                events.Add(Record(db, statement.BillNumber, BillEventKinds.Statement, key, now, FollowRules.StatementIsSendable(statement.FirstPublicAt, now)));
                recorded++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var sent = 0;
        var emails = 0;
        var waiting = events.Where(e => e.Outcome == BillEventOutcomes.Waiting).OrderBy(e => e.FirstSeen).ToList();
        var facts = waiting.Count == 0 ? new Dictionary<string, BillFacts>() : await bills.FindManyAsync(waiting.Select(e => e.BillNumber).Distinct().ToList(), cancellationToken);
        var known = await organisations.FindManyAsync(ids, cancellationToken);
        foreach (var e in waiting)
        {
            if (shownByKey.GetValueOrDefault(e.Key) is not { } statement
                || known.GetValueOrDefault(statement.OrganisationId) is not { Status: OrganisationStatus.Active } organisation)
            {
                // No longer public, or its organisation blocked or closed meanwhile: never sent rather than sent late.
                e.Outcome = BillEventOutcomes.Skipped;
                continue;
            }

            var bill = facts.GetValueOrDefault(e.BillNumber) ?? new BillFacts(e.BillNumber, e.BillNumber);
            emails += await SendAsync(
                db,
                followersOf[FollowRules.IdKey(organisation.Id)].Where(f => FollowRules.IsStatementRecipient(f.CreatedAt, e.FirstSeen, statement.FirstPublicAt)),
                n => n.Statements,
                (person, _) => OutboxMessage.Create(MessageKinds.FollowStatementEmail, new FollowStatementEmail(
                    person.Email, organisation.Name, organisation.ConfirmedBy, organisation.ConfirmedDomain, bill.Number, bill.Title, statement.Stance,
                    "/seimas/" + Uri.EscapeDataString(bill.Number), AccountPath)),
                transaction,
                cancellationToken);
            e.Outcome = BillEventOutcomes.Sent;
            e.SentAt = now;
            sent++;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new FollowRunOutcome(recorded, sent, emails);
    }

    /// <summary>
    /// One email per person per event, to members whose switch for this kind is on. People, their switches and, for a vote,
    /// the MPs they follow and the ballots are read once for all of them. Returns how many emails were written.
    /// </summary>
    private async Task<int> SendAsync(
        NotificationsDb db,
        IEnumerable<Follow> recipients,
        Func<NoticeSetting, bool> switchOn,
        Func<ParticipantFacts, IReadOnlyList<FollowedMpChoice>, OutboxMessage> message,
        IDbContextTransaction transaction,
        CancellationToken ct,
        StageVoteFacts? vote = null)
    {
        var people = recipients.Select(f => f.UserId).Distinct().ToList();
        if (people.Count == 0)
        {
            return 0;
        }

        var switches = await db.NoticeSettings.AsNoTracking().Where(n => people.Contains(n.UserId)).ToDictionaryAsync(n => n.UserId, ct);
        people = people.Where(p => !switches.TryGetValue(p, out var setting) || switchOn(setting)).ToList();
        var members = await participants.FindManyAsync(people, ct);
        Func<Guid, IReadOnlyList<FollowedMpChoice>> choices = vote is null ? _ => [] : await FollowedMpChoicesAsync(db, members.Keys.ToList(), vote, ct);
        var count = 0;
        foreach (var userId in people)
        {
            if (members.GetValueOrDefault(userId) is not { } person)
            {
                continue;
            }

            await outbox.EnqueueAsync(message(person, choices(userId)), transaction.GetDbTransaction(), ct);
            count++;
        }

        return count;
    }

    /// <summary>For a voted email: how each MP a person follows voted (public data), with every lookup made once.</summary>
    private async Task<Func<Guid, IReadOnlyList<FollowedMpChoice>>> FollowedMpChoicesAsync(
        NotificationsDb db, IReadOnlyCollection<Guid> people, StageVoteFacts vote, CancellationToken ct)
    {
        var follows = await db.Follows.AsNoTracking()
            .Where(f => people.Contains(f.UserId) && f.Kind == FollowKinds.Mp)
            .ToListAsync(ct);
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

/// <summary><see cref="IAccountData"/> for the follows (design 8.11.2): exported and erased with the account.</summary>
internal sealed class FollowAccountData(IDbContextFactory<NotificationsDb> contexts) : IAccountData
{
    public string Section => "follows";

    public async Task<object> ExportAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var follows = await db.Follows.AsNoTracking().Where(f => f.UserId == userId).OrderBy(f => f.CreatedAt)
            .Select(f => new { kind = f.Kind, key = f.Key, followedAt = f.CreatedAt }).ToListAsync(cancellationToken);
        var notices = await db.NoticeSettings.AsNoTracking().Where(n => n.UserId == userId)
            .Select(n => new { planned = n.Planned, voted = n.Voted, statements = n.Statements, changedAt = n.ChangedAt }).FirstOrDefaultAsync(cancellationToken);
        return new { follows, emails = notices };
    }

    public async Task EraseAsync(Guid userId, DbTransaction transaction, CancellationToken cancellationToken)
    {
        if (transaction is not NpgsqlTransaction { Connection: { } connection } npgsql)
        {
            throw new ArgumentException("Erasing needs a PostgreSQL transaction.", nameof(transaction));
        }

        foreach (var sql in new[] { "DELETE FROM notify.follow WHERE user_id = @user", "DELETE FROM notify.notice_setting WHERE user_id = @user" })
        {
#pragma warning disable CA2100 // Constant SQL.
            await using var command = new NpgsqlCommand(sql, connection, npgsql);
#pragma warning restore CA2100
            command.Parameters.AddWithValue("user", userId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
