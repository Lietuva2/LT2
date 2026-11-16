using System.Data.Common;
using System.Globalization;
using Lt2.Notifications.Infrastructure;
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
        var members = new List<FollowedMember>();
        foreach (var id in memberIds)
        {
            if ((await profiles.FindAsync(id, cancellationToken)).Profile is { } profile)
            {
                members.Add(new FollowedMember(profile.ProfileId, profile.DisplayName));
            }
        }

        return new MyFollows(
            numbers.Select(n => new FollowedBill(
                    facts.GetValueOrDefault(n) ?? new BillFacts(n, n),
                    planned.GetValueOrDefault(n),
                    votes.GetValueOrDefault(n)))
                .ToList(),
            ids.Select(id => people.GetValueOrDefault(id)).OfType<MpFacts>().ToList(),
            organisationIds.Select(id => known.GetValueOrDefault(id)).OfType<OrganisationFacts>().ToList(),
            members);
    }

    public async Task<IReadOnlySet<string>> FollowedBillNumbersAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var numbers = await db.Follows.AsNoTracking().Where(f => f.UserId == userId && f.Kind == FollowKinds.Bill).Select(f => f.Key).ToListAsync(cancellationToken);
        return numbers.ToHashSet(StringComparer.Ordinal);
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
        return state != ProfileIdState.Public || profile is null || profile.UserId == userId
            ? Error.NotFound("member.not-found")
            : await AddAsync(userId, FollowKinds.Member, profile.ProfileId, cancellationToken);
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

        // Under the delivery's lock, so a run never reads the choice half changed.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended({0}, 0))", ["lt2.follows.delivery"], cancellationToken);
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

    private static List<long> MpIds(IEnumerable<Follow> follows) =>
        follows.Where(f => f.Kind == FollowKinds.Mp).Select(f => long.Parse(f.Key, CultureInfo.InvariantCulture)).ToList();

    private static List<Guid> OrganisationIds(IEnumerable<Follow> follows) =>
        follows.Where(f => f.Kind == FollowKinds.Organisation).Select(f => Guid.TryParse(f.Key, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).Distinct().ToList();

    /// <summary>Adds a follow; following again changes nothing. There is no limit on how much a person follows (design 8.11.2).</summary>
    private async Task<Result<Unit>> AddAsync(Guid userId, string kind, string key, CancellationToken ct)
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
        if (await db.Follows.AnyAsync(f => f.UserId == userId && f.Kind == kind && f.Key == key, ct))
        {
            return Result.Ok();
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
            .Select(n => new { planned = n.Planned, voted = n.Voted, statements = n.Statements, responses = n.Responses, members = n.Members, comments = n.Comments, frequency = n.Frequency, frequencySince = n.FrequencySince, changedAt = n.ChangedAt }).FirstOrDefaultAsync(cancellationToken);
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
