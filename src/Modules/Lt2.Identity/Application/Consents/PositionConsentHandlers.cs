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
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Error.Unauthorized("auth.required");
        }

        if (textVersion != PositionConsent.TextVersion)
        {
            // The page showed an older wording: the person must see the text in force.
            return Error.Conflict("consent.outdated");
        }

        var now = clock.GetUtcNow();
        var current = await db.Consents.AnyAsync(
            c => c.UserId == userId && c.Kind == PositionConsent.Kind && c.TextVersion == textVersion && c.WithdrawnAt == null,
            cancellationToken);
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
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
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
/// <see cref="IParticipants"/> for the Appeals module. Plain SQL on its own connection per call, so it can be asked
/// from several components rendering at once.
/// </summary>
internal sealed class ParticipantDirectory(NpgsqlDataSource dataSource) : IParticipants
{
    public async Task<ParticipantFacts?> FindAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT p.display_name, u.email, p.registered_at,
                   EXISTS (SELECT 1 FROM identity.consent c
                           WHERE c.user_id = p.user_id AND c.kind = @kind AND c.text_version = @version AND c.withdrawn_at IS NULL)
            FROM identity.profile p JOIN identity."user" u ON u.id = p.user_id
            WHERE p.user_id = @id AND p.registered_at IS NOT NULL
            """);
        command.Parameters.AddWithValue("id", userId);
        command.Parameters.AddWithValue("kind", PositionConsent.Kind);
        command.Parameters.AddWithValue("version", PositionConsent.TextVersion);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ParticipantFacts(
            userId,
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.GetString(1),
            reader.GetFieldValue<DateTimeOffset>(2),
            reader.GetBoolean(3));
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