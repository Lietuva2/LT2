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
/// <see cref="IPositionErasure"/>, owned by Appeals).
/// </summary>
internal sealed class PositionConsentHandlers(IdentityDb db, IPositionErasure erasure, TimeProvider clock)
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
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Consents
            .Where(c => c.UserId == userId && c.Kind == PositionConsent.Kind && c.WithdrawnAt == null)
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
/// and switching the profile off withdraw it. Audited without the party.
/// </summary>
internal sealed class PartySupportHandlers(IdentityDb db, IParticipants participants, AccountEvents events, TimeProvider clock)
{
    public const string ConsentKind = "party-support";
    public const string ConsentVersion = "2026-11-13";

    public async Task<Result<PartySupportState>> GetAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (principal.UserId() is not { } userId)
        {
            return Error.Unauthorized("auth.required");
        }

        var party = await db.PartySupports.AsNoTracking().Where(p => p.UserId == userId).Select(p => p.Party).FirstOrDefaultAsync(cancellationToken);
        var profilePublic = await db.Profiles.AsNoTracking().AnyAsync(p => p.UserId == userId && p.PublicProfileId != null, cancellationToken);
        var shown = party is not null && profilePublic && await ShownConsents(userId).AnyAsync(cancellationToken);
        return new PartySupportState(party, shown, profilePublic, PartyPublicConsent.TextVersion);
    }

    /// <summary>
    /// "Rodyti viešame profilyje" ticked (design 8.25.9): the consent <c>party-public</c> against the text in force, only with a
    /// party and a public profile; never for the MP's own account. Giving it again changes nothing.
    /// </summary>
    public async Task<Result<Unit>> ShowAsync(ClaimsPrincipal principal, string? textVersion, string? ipHash, CancellationToken cancellationToken)
    {
        if (principal.UserId() is not { } userId)
        {
            return Error.Unauthorized("auth.required");
        }

        if (textVersion != PartyPublicConsent.TextVersion)
        {
            // The form showed another wording: the person must see the text in force.
            return Error.Conflict("party.consent-outdated");
        }

        if (MpAccounts.Refusal(await participants.FindAsync(userId, cancellationToken)) is { } refused)
        {
            return refused;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Under the profile's lock: switching the profile off at the same moment cannot leave the consent behind.
        await IdentityLocks.ProfileAsync(db, userId, cancellationToken);
        if (!await db.PartySupports.AnyAsync(p => p.UserId == userId, cancellationToken))
        {
            return Error.Conflict("party.none");
        }

        if (!await db.Profiles.AnyAsync(p => p.UserId == userId && p.PublicProfileId != null, cancellationToken))
        {
            return Error.Conflict("party.profile-off");
        }

        if (!await ShownConsents(userId).AnyAsync(cancellationToken))
        {
            var now = clock.GetUtcNow();
            db.Consents.Add(new Consent { Id = Ids.New(clock), UserId = userId, Kind = PartyPublicConsent.Kind, TextVersion = PartyPublicConsent.TextVersion, GivenAt = now });
            await db.SaveChangesAsync(cancellationToken);
            await events.RecordAsync(transaction.GetDbTransaction(), "party.public-on", userId, new Actor(userId, Roles.Member, ipHash), null, null, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>"Rodyti viešame profilyje" unticked: the consent withdrawn at once; open behind the terms (design 8.22.2).</summary>
    public async Task<Result<Unit>> HideAsync(ClaimsPrincipal principal, string? ipHash, CancellationToken cancellationToken)
    {
        if (principal.UserId() is not { } userId)
        {
            return Error.Unauthorized("auth.required");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await WithdrawShownAsync(userId, "box", new Actor(userId, Roles.Member, ipHash), transaction.GetDbTransaction(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }

    /// <summary>
    /// Withdraws <see cref="PartyPublicConsent"/> in the caller's transaction, audited as <c>party.public-off</c> with why
    /// (<paramref name="reason"/>: <c>box</c>, <c>party</c>, <c>profile</c>) and never the party; nothing when none was in force.
    /// </summary>
    public async Task WithdrawShownAsync(Guid userId, string reason, Actor actor, System.Data.Common.DbTransaction transaction, CancellationToken cancellationToken)
    {
        var withdrawn = await ShownConsents(userId).ExecuteUpdateAsync(s => s.SetProperty(c => c.WithdrawnAt, clock.GetUtcNow()), cancellationToken);
        if (withdrawn > 0)
        {
            await events.RecordAsync(transaction, "party.public-off", userId, actor, new { reason }, null, cancellationToken);
        }
    }

    private IQueryable<Consent> ShownConsents(Guid userId) =>
        db.Consents.Where(c => c.UserId == userId && c.Kind == PartyPublicConsent.Kind && c.WithdrawnAt == null);

    public async Task<Result<Unit>> SetAsync(ClaimsPrincipal principal, string? party, bool consent, IReadOnlyCollection<string> allowed, CancellationToken cancellationToken)
    {
        if (principal.UserId() is not { } userId)
        {
            return Error.Unauthorized("auth.required");
        }

        if (party is not null && !allowed.Contains(party))
        {
            return Error.Validation("party.unknown", "party");
        }

        // A party is a political opinion: stored only with the explicit consent given with it (design 5.2, 6.8).
        if (party is not null && !consent)
        {
            return Error.Validation("party.consent-required", "consent");
        }

        // Design 8.5.4: the MP's own account declares no party support (removing one it gave before is allowed).
        if (party is not null && MpAccounts.Refusal(await participants.FindAsync(userId, cancellationToken)) is { } refused)
        {
            return refused;
        }

        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await IdentityLocks.ProfileAsync(db, userId, cancellationToken);
        var before = await db.PartySupports.AsNoTracking().Where(p => p.UserId == userId).Select(p => p.Party).FirstOrDefaultAsync(cancellationToken);

        // Design 8.25.9: the consent to show a party covers the party it was given for; removing or changing it withdraws it.
        if (before != party)
        {
            await WithdrawShownAsync(userId, "party", new Actor(userId, Roles.Member, null), transaction.GetDbTransaction(), cancellationToken);
        }

        await db.PartySupports.Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        if (party is null)
        {
            await db.Consents.Where(c => c.UserId == userId && c.Kind == ConsentKind && c.WithdrawnAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.WithdrawnAt, now), cancellationToken);
        }
        else
        {
            db.PartySupports.Add(new PartySupport { UserId = userId, Party = party, GivenAt = now });
            if (!await db.Consents.AnyAsync(c => c.UserId == userId && c.Kind == ConsentKind && c.WithdrawnAt == null, cancellationToken))
            {
                db.Consents.Add(new Consent { Id = Ids.New(clock), UserId = userId, Kind = ConsentKind, TextVersion = ConsentVersion, GivenAt = now });
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Result.Ok();
    }
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
                   EXISTS (SELECT 1 FROM identity.politician_link l WHERE l.user_id = p.user_id AND l.part = 'mp' AND l.ended_at IS NULL)
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
                reader.GetBoolean(5));
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