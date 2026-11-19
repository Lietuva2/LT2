using System.Security.Claims;
using Lt2.Identity.Application.Staff;
using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Lt2.Identity.Application.Consents;

/// <summary>
/// The explicit consent before a first position or a first "Man svarbu" (design 6.5): given against a versioned text and
/// stored; withdrawing it (every version given) deletes the person's positions and marks in the same transaction (through
/// <see cref="IPositionErasure"/>, owned by Appeals). A context of its own per operation.
/// </summary>
internal sealed class PositionConsentHandlers(IDbContextFactory<IdentityDb> contexts, IPositionErasure erasure, TimeProvider clock)
{
    public async Task<Result<Unit>> GiveAsync(ClaimsPrincipal principal, string textVersion, CancellationToken cancellationToken)
    {
        if (principal.UserId() is not { } userId)
        {
            return Error.Unauthorized("auth.required");
        }

        if (textVersion != PositionConsent.TextVersion)
        {
            // The page showed an older wording: the person must see the text in force.
            return Error.Conflict("consent.outdated");
        }

        var now = clock.GetUtcNow();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var current = await CurrentPositionConsent.Of(db.Consents, userId).AnyAsync(cancellationToken);
        if (!current)
        {
            db.Consents.Add(new Consent
            {
                Id = Ids.New(clock),
                UserId = userId,
                Kind = PositionConsent.Kind,
                TextVersion = textVersion,
                GivenAt = now,
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result.Ok();
    }

    public async Task<Result<Unit>> WithdrawAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (principal.UserId() is not { } userId)
        {
            return Error.Unauthorized("auth.required");
        }

        var now = clock.GetUtcNow();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await CurrentPositionConsent.Live(db.Consents, userId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.WithdrawnAt, now), cancellationToken);
        await erasure.EraseAsync(userId, transaction.GetDbTransaction(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }
}

/// <summary>
/// Party support (design 5.2): private, optional, one party per person, overwritten without history. Giving it records an
/// explicit consent (kind <c>party-support</c>), since a party preference is a political opinion too; removing it deletes
/// the row and withdraws the consent. Showing it on the public profile (design 8.25.9, owner 2026-11-17) is its own consent,
/// <see cref="PartyPublicConsent"/>, given only while the profile is public; removing or changing the party, unticking the box
/// and switching the profile off withdraw it. Audited without the party. Both are given and changed through the "Profilis"
/// form (<c>ProfileHandlers.SaveAsync</c>, design 8.28.5), whose rules are <see cref="ProfileFormRules"/>, in its context and
/// transaction; the two withdrawals stand on their own, open behind the terms (design 8.22.2), each in a context of its own.
/// </summary>
internal sealed class PartySupportHandlers(IDbContextFactory<IdentityDb> contexts, AccountEvents events, TimeProvider clock)
{
    public const string ConsentKind = "party-support";
    public const string ConsentVersion = "2026-11-13";

    /// <summary>The party the person supports, or null.</summary>
    public static Task<string?> PartyAsync(IdentityDb db, Guid userId, CancellationToken cancellationToken) =>
        db.PartySupports.AsNoTracking().Where(p => p.UserId == userId).Select(p => p.Party).FirstOrDefaultAsync(cancellationToken);

    /// <summary>Whether the consent <c>party-public</c> is in force.</summary>
    public static Task<bool> ShownAsync(IdentityDb db, Guid userId, CancellationToken cancellationToken) =>
        ShownConsents(db, userId).AnyAsync(cancellationToken);

    /// <summary>"Rodyti viešame profilyje" unticked: the consent withdrawn at once; open behind the terms (design 8.22.2).</summary>
    public async Task<Result<Unit>> HideAsync(ClaimsPrincipal principal, string? ipHash, CancellationToken cancellationToken)
    {
        if (principal.UserId() is not { } userId)
        {
            return Error.Unauthorized("auth.required");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await WithdrawShownAsync(db, userId, "box", new Actor(userId, Roles.Member, ipHash), transaction.GetDbTransaction(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>
    /// The party support withdrawn (design 5.2): the party removed, its consent withdrawn, the party no longer shown; open behind
    /// the terms (design 8.22.2) and to the MP's own account. Changes nothing when there is none.
    /// </summary>
    public async Task<Result<Unit>> WithdrawAsync(ClaimsPrincipal principal, string? ipHash, CancellationToken cancellationToken)
    {
        if (principal.UserId() is not { } userId)
        {
            return Error.Unauthorized("auth.required");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await IdentityLocks.ProfileAsync(db, userId, cancellationToken);
        var before = await PartyAsync(db, userId, cancellationToken);
        await ChangeAsync(db, userId, before, null, new Actor(userId, Roles.Member, ipHash), transaction.GetDbTransaction(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>
    /// Sets the party (already checked: one of the parties, with its consent, not the MP's own account) or with null removes it
    /// and withdraws its consent, in the caller's context and transaction. The consent to show a party covers the party it was
    /// given for (design 8.25.9): a removed or changed party withdraws it.
    /// </summary>
    public async Task ChangeAsync(IdentityDb db, Guid userId, string? before, string? party, Actor actor, System.Data.Common.DbTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (before != party)
        {
            await WithdrawShownAsync(db, userId, "party", actor, transaction, cancellationToken);
        }

        var now = clock.GetUtcNow();
        await db.PartySupports.Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        if (party is null)
        {
            await db.Consents.Where(c => c.UserId == userId && c.Kind == ConsentKind && c.WithdrawnAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.WithdrawnAt, now), cancellationToken);
            return;
        }

        db.PartySupports.Add(new PartySupport { UserId = userId, Party = party, GivenAt = now });
        if (!await db.Consents.AnyAsync(c => c.UserId == userId && c.Kind == ConsentKind && c.WithdrawnAt == null, cancellationToken))
        {
            db.Consents.Add(new Consent { Id = Ids.New(clock), UserId = userId, Kind = ConsentKind, TextVersion = ConsentVersion, GivenAt = now });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// "Rodyti viešame profilyje" ticked (design 8.25.9; already checked: the text in force, a party, a public profile, not the
    /// MP's own account): the consent <c>party-public</c>, in the caller's context and transaction. Giving it again changes nothing.
    /// </summary>
    public async Task GiveShownAsync(IdentityDb db, Guid userId, Actor actor, System.Data.Common.DbTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (await ShownAsync(db, userId, cancellationToken))
        {
            return;
        }

        db.Consents.Add(new Consent
        {
            Id = Ids.New(clock), UserId = userId, Kind = PartyPublicConsent.Kind, TextVersion = PartyPublicConsent.TextVersion, GivenAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
        await events.RecordAsync(transaction, "party.public-on", userId, actor, null, null, cancellationToken);
    }

    /// <summary>
    /// Withdraws <see cref="PartyPublicConsent"/> in the caller's context and transaction, audited as <c>party.public-off</c> with why
    /// (<paramref name="reason"/>: <c>box</c>, <c>party</c>, <c>profile</c>) and never the party; nothing when none was in force.
    /// </summary>
    public async Task WithdrawShownAsync(IdentityDb db, Guid userId, string reason, Actor actor, System.Data.Common.DbTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var withdrawn = await ShownConsents(db, userId).ExecuteUpdateAsync(s => s.SetProperty(c => c.WithdrawnAt, clock.GetUtcNow()), cancellationToken);
        if (withdrawn > 0)
        {
            await events.RecordAsync(transaction, "party.public-off", userId, actor, new { reason }, null, cancellationToken);
        }
    }

    private static IQueryable<Consent> ShownConsents(IdentityDb db, Guid userId) =>
        db.Consents.Where(c => c.UserId == userId && c.Kind == PartyPublicConsent.Kind && c.WithdrawnAt == null);
}

/// <summary>
/// <see cref="IParticipants"/> for the Appeals module. Plain SQL on its own connection per call, so it can be asked
/// from several components rendering at once.
/// </summary>
internal sealed class ParticipantDirectory(NpgsqlDataSource dataSource) : IParticipants
{
    public async Task<ParticipantFacts?> FindAsync(Guid userId, CancellationToken cancellationToken)
    {
        var found = await FindManyAsync([userId], cancellationToken);
        return found.GetValueOrDefault(userId);
    }

    public async Task<IReadOnlyDictionary<Guid, ParticipantFacts>> FindManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, ParticipantFacts>();
        if (userIds.Count == 0)
        {
            return result;
        }

#pragma warning disable CA2100 // Constant SQL: the consent condition is a constant of this module.
        await using var command = dataSource.CreateCommand(
            $"""
            SELECT p.user_id, p.display_name, u.email, p.registered_at, {CurrentPositionConsent.ExistsSql},
                   EXISTS (SELECT 1 FROM identity.politician_link l WHERE l.user_id = p.user_id AND l.part = 'mp' AND l.ended_at IS NULL),
                   p.locked_at IS NOT NULL
            FROM identity.profile p JOIN identity."user" u ON u.id = p.user_id
            WHERE p.user_id = ANY(@ids) AND p.registered_at IS NOT NULL
            """);
#pragma warning restore CA2100
        command.Parameters.AddWithValue("ids", userIds.Distinct().ToArray());
        CurrentPositionConsent.AddParameters(command);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetGuid(0);
            result[id] = new ParticipantFacts(
                id,
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3),
                reader.GetBoolean(4),
                reader.GetBoolean(5),
                reader.GetBoolean(6));
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<Guid, string?>> DisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, string?>();
        if (userIds.Count == 0)
        {
            return result;
        }

        await using var command = dataSource.CreateCommand("SELECT user_id, display_name FROM identity.profile WHERE user_id = ANY(@ids)");
        command.Parameters.AddWithValue("ids", userIds.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result[reader.GetGuid(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
        }

        return result;
    }
}

/// <summary>
/// <see cref="IStaffAddresses"/> for the notice of a prepared summary (design 8.19.2): the accounts with an activated moderator
/// or administrator role and a staff passkey, as <c>StaffAccess</c> reads them. Plain SQL, a connection per call.
/// </summary>
internal sealed class StaffAddressDirectory(NpgsqlDataSource dataSource) : IStaffAddresses
{
    public async Task<IReadOnlyList<string>> ModeratorAddressesAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT DISTINCT u.email FROM identity."user" u
            JOIN identity.profile p ON p.user_id = u.id AND p.staff_passkey_id IS NOT NULL
            JOIN identity.user_role ur ON ur.user_id = u.id
            JOIN identity.role r ON r.id = ur.role_id AND r.name = ANY(@roles)
            WHERE u.email IS NOT NULL
              AND EXISTS (SELECT 1 FROM identity.role_activation a WHERE a.user_id = u.id AND a.role = r.name AND a.used_at IS NOT NULL)
            """);
        command.Parameters.AddWithValue("roles", new[] { Roles.Moderator, Roles.Administrator });
        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }
}

/// <summary><see cref="IPartySupporters"/> for the Appeals module's group numbers. Plain SQL, a connection per call.</summary>
internal sealed class PartySupporterDirectory(NpgsqlDataSource dataSource) : IPartySupporters
{
    public async Task<IReadOnlyDictionary<string, IReadOnlyCollection<Guid>>> SupportersByPartyAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
        await using var command = dataSource.CreateCommand("SELECT party, user_id FROM identity.party_support");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var party = reader.GetString(0);
            if (!result.TryGetValue(party, out var users))
            {
                result[party] = users = [];
            }

            users.Add(reader.GetGuid(1));
        }

        return result.ToDictionary(r => r.Key, r => (IReadOnlyCollection<Guid>)r.Value, StringComparer.Ordinal);
    }
}