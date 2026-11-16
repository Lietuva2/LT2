using System.Security.Claims;
using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Lt2.Identity.Application.Consents;

/// <summary>
/// The explicit consent before a first position (design 6.5): given against a versioned text and stored; withdrawing it
/// deletes the person's positions in the same transaction (through <see cref="IPositionErasure"/>, owned by Appeals).
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
/// the row and withdraws the consent.
/// </summary>
internal sealed class PartySupportHandlers(IdentityDb db, IParticipants participants, TimeProvider clock)
{
    public const string ConsentKind = "party-support";
    public const string ConsentVersion = "2026-11-13";

    public async Task<Result<string?>> GetAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (principal.UserId() is not { } userId)
        {
            return Error.Unauthorized("auth.required");
        }

        return await db.PartySupports.AsNoTracking().Where(p => p.UserId == userId).Select(p => p.Party).FirstOrDefaultAsync(cancellationToken);
    }

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