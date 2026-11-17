using System.Data.Common;
using System.Globalization;
using Lt2.Notifications.Infrastructure;
using Lt2.Notifications.MpMail;
using Lt2.Notifications.Rhythm;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Lt2.Notifications.Follows;

/// <summary>
/// Following bills and MPs (design 8.11.2) and organisations (design 8.14.14, stage B), and the follow emails. Reads the Seimas
/// through <see cref="IBillCatalogue"/>, <see cref="IPlannedAgenda"/> and <see cref="IMpDirectory"/>, organisations through
/// <see cref="IOrganisationDirectory"/>, MPs' responses through <see cref="IMpResponseFeed"/> (design 8.20.1), people through
/// <see cref="IParticipants"/> and <see cref="IMpLinks"/>; never a side.
/// Logs carry counts only: no address, no person, no bill a person follows.
/// </summary>
internal sealed class FollowService(
    IDbContextFactory<NotificationsDb> contexts,
    IParticipants participants,
    IBillCatalogue bills,
    IPlannedAgenda agenda,
    IMpDirectory mps,
    IOrganisationDirectory organisations,
    IPublicProfiles profiles,
    NoticeDelivery delivery,
    IAuditLog audit,
    TimeProvider clock,
    ILogger<FollowService> logger) : IFollows
{

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
        var memberIds = follows.Where(f => f.Kind == FollowKinds.Member).Select(f => f.Key).ToList();
        var shownMembers = memberIds.Count == 0 ? new Dictionary<string, PublicProfileFacts>() : await profiles.PublicByIdsAsync(memberIds, cancellationToken);
        var members = memberIds.Select(id => shownMembers.GetValueOrDefault(id)).OfType<PublicProfileFacts>().Select(p => new FollowedMember(p.ProfileId, p.DisplayName)).ToList();

        return new MyFollows(
            follows.Where(f => f.Kind == FollowKinds.Bill).Select(f => new FollowedBill(
                    facts.GetValueOrDefault(f.Key) ?? new BillFacts(f.Key, f.Key),
                    planned.GetValueOrDefault(f.Key),
                    votes.GetValueOrDefault(f.Key),
                    f.LastSeenAt))
                .ToList(),
            ids.Select(id => people.GetValueOrDefault(id)).OfType<MpFacts>().ToList(),
            organisationIds.Select(id => known.GetValueOrDefault(id)).OfType<OrganisationFacts>().ToList(),
            members);
    }

    public async Task<IReadOnlyDictionary<string, DateTimeOffset>> FollowedBillsAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await db.Follows.AsNoTracking().Where(f => f.UserId == userId && f.Kind == FollowKinds.Bill)
            .Select(f => new { f.Key, f.LastSeenAt }).ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Key, r => r.LastSeenAt, StringComparer.Ordinal);
    }

    public async Task<DateTimeOffset?> SeenBillAsync(Guid userId, string number, CancellationToken cancellationToken)
    {
        var key = number.Trim().ToUpperInvariant();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // The row locked, so two pages opened at once both answer a moment and the later one is kept.
        var follow = await db.Follows
            .FromSqlRaw("SELECT * FROM notify.follow WHERE user_id = {0} AND kind = {1} AND key = {2} FOR UPDATE", userId, FollowKinds.Bill, key)
            .FirstOrDefaultAsync(cancellationToken);
        if (follow is null)
        {
            return null;
        }

        var previous = follow.LastSeenAt;
        follow.LastSeenAt = FollowRules.Seen(previous, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return previous;
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
        return new NoticeSettings(row?.Planned ?? true, row?.Voted ?? true, row?.Statements ?? true, row?.Responses ?? true, row?.Members ?? true, row?.Comments ?? true);
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
        row.Responses = settings.Responses;
        row.Members = settings.Members;
        row.Comments = settings.Comments;
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

    public async Task<Result<Unit>> FollowMemberAsync(Guid userId, string profileId, CancellationToken cancellationToken)
    {
        // Only a profile that is public now can be followed (design 8.25.5), and not one's own: the same answer as an unknown id.
        var (state, profile) = await profiles.FindAsync(profileId, cancellationToken);
        if (state != ProfileIdState.Public || profile is null || profile.UserId == userId)
        {
            return Error.NotFound("member.not-found");
        }

        // Checked again under the profile's follow lock, which switching it off takes too (MemberFollowEnd): a switch-off that
        // committed meanwhile refuses the follow, and one still running ends it after this commits.
        return await AddAsync(userId, FollowKinds.Member, profile.ProfileId, cancellationToken, MemberFollowLock.Key(profile.ProfileId), async () =>
            (await profiles.FindAsync(profile.ProfileId, cancellationToken)).State == ProfileIdState.Public);
    }

    public async Task<Result<Unit>> UnfollowMemberAsync(Guid userId, string profileId, CancellationToken cancellationToken) =>
        await RemoveAsync(userId, FollowKinds.Member, profileId, cancellationToken);

    public async Task<string> FrequencyAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.NoticeSettings.AsNoTracking().Where(n => n.UserId == userId).Select(n => n.Frequency).FirstOrDefaultAsync(cancellationToken)
            ?? Frequencies.Immediate;
    }

    /// <summary>
    /// Changes how often the person's emails come (design 8.24). The choice it replaces is kept with its start, so what waited
    /// for that choice's email still comes in it; the same choice again changes nothing. Audited without the choice itself.
    /// </summary>
    public async Task<Result<Unit>> SetFrequencyAsync(Guid userId, string frequency, string? ipHash, CancellationToken cancellationToken)
    {
        if (!Frequencies.IsValid(frequency))
        {
            return Error.Validation("notices.frequency-invalid", "frequency");
        }

        if (await participants.FindAsync(userId, cancellationToken) is null)
        {
            return Error.Forbidden("member.required");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Under the person's own lock, so two saves at once build on each other. Not under the delivery's lock: a run reads
        // every choice in one query at its start and the change is one row's update, so a run sees the choice whole, before or
        // after; a save never waits for a run.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended({0}, 0))", ["lt2.follows.frequency:" + userId.ToString("N")], cancellationToken);
        var row = await db.NoticeSettings.FirstOrDefaultAsync(n => n.UserId == userId, cancellationToken);
        var current = row is null ? RhythmChoice.Default : new RhythmChoice(row.Frequency, row.FrequencySince, row.PreviousFrequency, row.PreviousSince);
        var now = clock.GetUtcNow();
        var changed = RhythmRules.Change(current, frequency, now);
        if (changed == current)
        {
            return Result.Ok();
        }

        if (row is null)
        {
            row = new NoticeSetting { UserId = userId };
            db.NoticeSettings.Add(row);
        }

        row.Frequency = changed.Frequency;
        row.FrequencySince = changed.Since;
        row.PreviousFrequency = changed.Previous;
        row.PreviousSince = changed.PreviousSince;
        row.ChangedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("notices.frequency-changed", userId, "member", null, null, ipHash), transaction.GetDbTransaction(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task StopByLinkAsync(string? token, string? ipHash, CancellationToken cancellationToken)
    {
        if (!MailKeys.LooksLikeToken(token))
        {
            return;
        }

        var hash = MailKeys.TokenHash(token!);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var userId = await db.MemberMailTokens.AsNoTracking().Where(t => t.TokenHash == hash).Select(t => (Guid?)t.UserId).FirstOrDefaultAsync(cancellationToken);
        if (userId is not { } id || await participants.FindAsync(id, cancellationToken) is null)
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var row = await db.NoticeSettings.FirstOrDefaultAsync(n => n.UserId == id, cancellationToken);
        if (row is { Planned: false, Voted: false, Statements: false, Responses: false, Members: false, Comments: false })
        {
            return;
        }

        if (row is null)
        {
            row = new NoticeSetting { UserId = id };
            db.NoticeSettings.Add(row);
        }

        row.Planned = row.Voted = row.Statements = row.Responses = row.Members = row.Comments = false;
        row.ChangedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        // Whoever holds the link acted, so the account is the subject, not the actor; no detail (design 8.24.1).
        await audit.WriteAsync(new AuditEntry("notices.stopped-by-link", null, null, id.ToString(), null, ipHash), transaction.GetDbTransaction(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// The worker's job: every kind records what is new, then the delivery makes the run's emails in the people's rhythms
    /// (design 8.24): the follow job itself knows no kind.
    /// </summary>
    public async Task<FollowRunOutcome> RunAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var recorded = await delivery.RecordAsync(now, cancellationToken);
        var delivered = await delivery.DeliverAsync(now, cancellationToken);
        var outcome = new FollowRunOutcome(recorded, delivered.Notices, delivered.Emails);
        logger.LogInformation("Follow job: {Recorded} events recorded, {Sent} things told, {Emails} emails", outcome.Recorded, outcome.Sent, outcome.Emails);
        return outcome;
    }

    public async Task<int> PurgeEventsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now - FollowRules.EventRetention;
        var today = FollowRules.PlannedKey(VilniusTime.DateOf(now));
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        // A planned date's key is its yyyy-MM-dd, so it compares as text.
        var events = await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM notify.bill_event WHERE first_seen < {0} AND (kind <> {1} OR key < {2})", [cutoff, BillEventKinds.Planned, today], cancellationToken);
        var passes = await db.RhythmPasses.Where(r => r.MadeAt < cutoff).ExecuteDeleteAsync(cancellationToken);
        return events + passes;
    }

    private static List<long> MpIds(IEnumerable<Follow> follows) =>
        follows.Where(f => f.Kind == FollowKinds.Mp).Select(f => long.Parse(f.Key, CultureInfo.InvariantCulture)).ToList();

    private static List<Guid> OrganisationIds(IEnumerable<Follow> follows) =>
        follows.Where(f => f.Kind == FollowKinds.Organisation).Select(f => Guid.TryParse(f.Key, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).Distinct().ToList();

    /// <summary>Adds a follow; following again changes nothing. There is no limit on how much a person follows (design 8.11.2).</summary>
    private async Task<Result<Unit>> AddAsync(Guid userId, string kind, string key, CancellationToken ct, string? thingLock = null, Func<Task<bool>>? stillAllowed = null)
    {
        if (await participants.FindAsync(userId, ct) is null)
        {
            return Error.Forbidden("member.required");
        }

        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Under the person's own lock, so two requests at once (a double press, a first side and "Sekti") make one follow
        // and never hit the primary key.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended({0}, 0))", ["lt2.follows:" + userId.ToString("N")], ct);
        if (thingLock is not null)
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended({0}, 0))", [thingLock], ct);
            if (stillAllowed is not null && !await stillAllowed())
            {
                return Error.NotFound(kind + ".not-found");
            }
        }

        if (await db.Follows.AnyAsync(f => f.UserId == userId && f.Kind == kind && f.Key == key, ct))
        {
            return Result.Ok();
        }

        var now = clock.GetUtcNow();
        db.Follows.Add(new Follow { UserId = userId, Kind = kind, Key = key, CreatedAt = now, LastSeenAt = now });
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
}
/// <summary><see cref="IAccountData"/> for the follows (design 8.11.2): exported and erased with the account.</summary>
internal sealed class FollowAccountData(IDbContextFactory<NotificationsDb> contexts) : IAccountData
{
    public string Section => "follows";

    public async Task<object> ExportAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var follows = await db.Follows.AsNoTracking().Where(f => f.UserId == userId).OrderBy(f => f.CreatedAt)
            .Select(f => new { kind = f.Kind, key = f.Key, followedAt = f.CreatedAt, lastSeenAt = f.Kind == FollowKinds.Bill ? f.LastSeenAt : (DateTimeOffset?)null })
            .ToListAsync(cancellationToken);
        var notices = await db.NoticeSettings.AsNoTracking().Where(n => n.UserId == userId)
            .Select(n => new { planned = n.Planned, voted = n.Voted, statements = n.Statements, responses = n.Responses, members = n.Members, comments = n.Comments, frequency = n.Frequency, frequencySince = n.FrequencySince, previousFrequency = n.PreviousFrequency, previousSince = n.PreviousSince, changedAt = n.ChangedAt }).FirstOrDefaultAsync(cancellationToken);
        // Design 8.19.3 (owner, 2026-11-17): an office member's own choice of copies of the MP's summaries.
        var officeCopies = await db.OfficeCopies.AsNoTracking().Where(c => c.UserId == userId)
            .Select(c => new { mpSeimasPersonId = c.SeimasPersonId, frequency = c.Frequency, changedAt = c.ChangedAt }).ToListAsync(cancellationToken);
        return new { follows, emails = notices, officeCopies };
    }

    public async Task EraseAsync(Guid userId, DbTransaction transaction, CancellationToken cancellationToken)
    {
        if (transaction is not NpgsqlTransaction { Connection: { } connection } npgsql)
        {
            throw new ArgumentException("Erasing needs a PostgreSQL transaction.", nameof(transaction));
        }

        // The stop link (design 8.24.1) goes with the account; it is not exported, being a key and not the person's data.
        foreach (var sql in new[]
                 {
                     "DELETE FROM notify.follow WHERE user_id = @user", "DELETE FROM notify.notice_setting WHERE user_id = @user",
                     "DELETE FROM notify.member_mail_token WHERE user_id = @user",

                     // An office member's copies (design 8.19.3): the choice, and the delivery rows that name the account.
                     "DELETE FROM notify.office_copy WHERE user_id = @user",
                     "DELETE FROM notify.digest_delivery WHERE recipient = 'office' AND office_user_id = @user",
                 })
        {
#pragma warning disable CA2100 // Constant SQL.
            await using var command = new NpgsqlCommand(sql, connection, npgsql);
#pragma warning restore CA2100
            command.Parameters.AddWithValue("user", userId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
