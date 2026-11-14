using System.Data.Common;
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

    public async Task<MyFollows> MineAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var follows = await db.Follows.AsNoTracking().Where(f => f.UserId == userId).OrderBy(f => f.CreatedAt).ToListAsync(cancellationToken);
        var numbers = follows.Where(f => f.Kind == FollowKinds.Bill).Select(f => f.Key).ToList();
        var facts = numbers.Count == 0 ? new Dictionary<string, BillFacts>() : await bills.FindManyAsync(numbers, cancellationToken);
        var planned = numbers.Count == 0 ? new Dictionary<string, PlannedDate>() : await agenda.ForBillsAsync(numbers, cancellationToken);
        var votes = numbers.Count == 0 ? [] : await bills.StageResultsAsync(numbers, DateTimeOffset.MinValue, cancellationToken);
        var ids = follows.Where(f => f.Kind == FollowKinds.Mp).Select(f => long.Parse(f.Key, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        var people = ids.Count == 0 ? new Dictionary<long, MpFacts>() : await mps.MpsAsync(ids, cancellationToken);
        var organisationIds = OrganisationIds(follows);
        var known = await organisations.FindManyAsync(organisationIds, cancellationToken);
        return new MyFollows(
            numbers.Select(n => new FollowedBill(
                    facts.GetValueOrDefault(n) ?? new BillFacts(n, n),
                    planned.GetValueOrDefault(n),
                    votes.Where(v => v.BillNumber == n).OrderByDescending(v => v.HeldAt).FirstOrDefault()))
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
        return mp is null ? Error.NotFound("mp.not-found") : await AddAsync(userId, FollowKinds.Mp, FollowRules.VotedKey(mp.SeimasPersonId), cancellationToken);
    }

    public async Task<Result<Unit>> UnfollowMpAsync(Guid userId, string slug, CancellationToken cancellationToken)
    {
        var mp = await mps.FindMpAsync(slug, cancellationToken);
        return mp is null ? Result.Ok() : await RemoveAsync(userId, FollowKinds.Mp, FollowRules.VotedKey(mp.SeimasPersonId), cancellationToken);
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
        return organisation is not { Status: OrganisationCodeStates.Active }
            ? Error.NotFound("organisation.not-found")
            : await AddAsync(userId, FollowKinds.Organisation, OrganisationKey(organisation.Id), cancellationToken);
    }

    public async Task<Result<Unit>> UnfollowOrganisationAsync(Guid userId, string slug, CancellationToken cancellationToken)
    {
        var organisation = await organisations.FindAsync(slug, cancellationToken);
        return organisation is null ? Result.Ok() : await RemoveAsync(userId, FollowKinds.Organisation, OrganisationKey(organisation.Id), cancellationToken);
    }

    private static string OrganisationKey(Guid id) => id.ToString("D", System.Globalization.CultureInfo.InvariantCulture);

    private static List<Guid> OrganisationIds(IEnumerable<Follow> follows) =>
        follows.Where(f => f.Kind == FollowKinds.Organisation).Select(f => Guid.TryParse(f.Key, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).Distinct().ToList();

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

    public async Task<FollowRunOutcome> RunAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var bills = await RunBillsAsync(now, cancellationToken);
        var statements = await RunStatementsAsync(now, cancellationToken);
        var outcome = new FollowRunOutcome(bills.Recorded + statements.Recorded, bills.Sent + statements.Sent, bills.Emails + statements.Emails);
        logger.LogInformation("Follow job: {Recorded} events recorded, {Sent} sent, {Emails} emails", outcome.Recorded, outcome.Sent, outcome.Emails);
        return outcome;
    }

    /// <summary>Planned dates and stage votes of followed bills (design 8.11.2).</summary>
    private async Task<FollowRunOutcome> RunBillsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var billFollows = await db.Follows.AsNoTracking().Where(f => f.Kind == FollowKinds.Bill).ToListAsync(cancellationToken);
        var numbers = billFollows.Select(f => f.Key).Distinct().ToList();
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
            var followers = billFollows.Where(f => f.Key == waiting.BillNumber).ToList();
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
                emails += await SendAsync(db, followers.Where(f => FollowRules.IsRecipient(f.CreatedAt, waiting.FirstSeen, null)), n => n.Planned,
                    to => OutboxMessage.Create(MessageKinds.FollowPlannedEmail, new FollowPlannedEmail(
                        to, bill.Number, bill.Title, plan.Stage, plan.Date, "/seimas/" + Uri.EscapeDataString(bill.Number), AccountPath)),
                    null, transaction, cancellationToken);
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

                emails += await SendAsync(db, followers.Where(f => FollowRules.IsRecipient(f.CreatedAt, waiting.FirstSeen, vote.HeldAt)), n => n.Voted,
                    _ => throw new InvalidOperationException("Voted emails are made per person."),
                    vote, transaction, cancellationToken, bill);
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
        var ids = OrganisationIds(organisationFollows);
        var waitingAlready = await db.BillEvents.AsNoTracking().AnyAsync(e => e.Kind == BillEventKinds.Statement && e.Outcome == BillEventOutcomes.Waiting, cancellationToken);
        if (ids.Count == 0 && !waitingAlready)
        {
            return new FollowRunOutcome(0, 0, 0);
        }

        var shown = await organisations.PublicStatementsAsync(ids, now - VoteLookBack, cancellationToken);
        var keys = shown.Select(s => StatementKey(s.Id)).ToList();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var events = await db.BillEvents
            .Where(e => e.Kind == BillEventKinds.Statement && (keys.Contains(e.Key) || e.Outcome == BillEventOutcomes.Waiting))
            .ToListAsync(cancellationToken);

        var recorded = 0;
        foreach (var statement in shown)
        {
            var key = StatementKey(statement.Id);
            if (!events.Any(e => e.Kind == BillEventKinds.Statement && e.Key == key))
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
            var statement = shown.FirstOrDefault(s => StatementKey(s.Id) == e.Key);
            if (statement is null || known.GetValueOrDefault(statement.OrganisationId) is not { Status: OrganisationCodeStates.Active } organisation)
            {
                // No longer public, or its organisation blocked or closed meanwhile: never sent rather than sent late.
                e.Outcome = BillEventOutcomes.Skipped;
                continue;
            }

            var bill = facts.GetValueOrDefault(e.BillNumber) ?? new BillFacts(e.BillNumber, e.BillNumber);
            var key = OrganisationKey(organisation.Id);
            emails += await SendAsync(
                db,
                organisationFollows.Where(f => f.Key == key && FollowRules.IsStatementRecipient(f.CreatedAt, e.FirstSeen, statement.FirstPublicAt)),
                n => n.Statements,
                to => OutboxMessage.Create(MessageKinds.FollowStatementEmail, new FollowStatementEmail(
                    to, organisation.Name, organisation.ConfirmedBy, organisation.ConfirmedDomain, bill.Number, bill.Title, statement.Stance,
                    "/seimas/" + Uri.EscapeDataString(bill.Number), AccountPath)),
                null,
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

    private static string StatementKey(Guid id) => id.ToString("D", System.Globalization.CultureInfo.InvariantCulture);

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

    /// <summary>One email per person per event, to those whose switch for this kind is on. Returns how many were written.</summary>
    private async Task<int> SendAsync(
        NotificationsDb db, IEnumerable<Follow> recipients, Func<NoticeSetting, bool> switchOn, Func<string, OutboxMessage> planned,
        StageVoteFacts? vote, IDbContextTransaction transaction, CancellationToken ct, BillFacts? bill = null)
    {
        var people = recipients.Select(f => f.UserId).Distinct().ToList();
        if (people.Count == 0)
        {
            return 0;
        }

        var switches = await db.NoticeSettings.AsNoTracking().Where(n => people.Contains(n.UserId)).ToDictionaryAsync(n => n.UserId, ct);
        var count = 0;
        foreach (var userId in people)
        {
            if (switches.TryGetValue(userId, out var setting) && !switchOn(setting))
            {
                continue;
            }

            if (await participants.FindAsync(userId, ct) is not { } person)
            {
                continue;
            }

            var message = vote is null ? planned(person.Email) : await VotedAsync(db, userId, person.Email, vote, bill!, ct);
            await outbox.EnqueueAsync(message, transaction.GetDbTransaction(), ct);
            count++;
        }

        return count;
    }

    /// <summary>The voted email for one person: the result, and how each MP they follow voted (public data).</summary>
    private async Task<OutboxMessage> VotedAsync(NotificationsDb db, Guid userId, string to, StageVoteFacts vote, BillFacts bill, CancellationToken ct)
    {
        var followed = (await db.Follows.AsNoTracking().Where(f => f.UserId == userId && f.Kind == FollowKinds.Mp).Select(f => f.Key).ToListAsync(ct))
            .Select(k => long.Parse(k, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
        var choices = new List<FollowedMpChoice>();
        if (followed.Count > 0)
        {
            var people = await mps.MpsAsync(followed, ct);
            var ballots = vote.HasResult ? await bills.ChoicesAsync(vote.VoteId, followed, ct) : new Dictionary<long, string>();
            choices = followed
                .Where(people.ContainsKey)
                .Select(id => new FollowedMpChoice(
                    people[id].FirstName + " " + people[id].LastName,
                    vote.HasResult ? ballots.GetValueOrDefault(id, "did-not-vote") : null))
                .OrderBy(c => c.Name, StringComparer.Create(new System.Globalization.CultureInfo("lt-LT"), false))
                .ToList();
        }

        return OutboxMessage.Create(MessageKinds.FollowVotedEmail, new FollowVotedEmail(
            to, bill.Number, bill.Title, vote.Stage, FollowRules.VilniusDay(vote.HeldAt), vote.IsConsensus, vote.For, vote.Against, vote.Abstain,
            vote.OfficialNote, "/seimas/balsavimai/" + vote.VoteId.ToString(System.Globalization.CultureInfo.InvariantCulture), choices, AccountPath));
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
