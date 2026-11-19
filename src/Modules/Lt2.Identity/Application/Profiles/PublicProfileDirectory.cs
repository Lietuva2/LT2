using System.Security.Claims;
using Lt2.Identity.Domain;
using Lt2.SharedKernel;
using Npgsql;

namespace Lt2.Identity.Application.Profiles;

/// <summary>
/// <see cref="IPublicProfiles"/> for the other modules and the host (design 8.25): only profiles that are public now, with the
/// name and, unless a moderator hid it, "Apie mane". Plain SQL on its own connection per call.
/// </summary>
internal sealed class PublicProfileDirectory(NpgsqlDataSource dataSource) : IPublicProfiles
{
    private const string Columns = "p.user_id, p.public_profile_id, p.display_name, p.about, p.about_hidden_at, p.public_since";

    public async Task<IReadOnlyDictionary<Guid, PublicProfileFacts>> ForAccountsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, PublicProfileFacts>();
        if (userIds.Count == 0)
        {
            return result;
        }

        await using var command = dataSource.CreateCommand(
            $"SELECT {Columns} FROM identity.profile p WHERE p.user_id = ANY(@ids) AND p.public_profile_id IS NOT NULL AND p.display_name IS NOT NULL AND p.locked_at IS NULL");
        command.Parameters.AddWithValue("ids", userIds.Distinct().ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var facts = Read(reader);
            result[facts.UserId] = facts;
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<string, PublicProfileFacts>> PublicByIdsAsync(IReadOnlyCollection<string> profileIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, PublicProfileFacts>(StringComparer.Ordinal);
        var ids = profileIds.Where(ProfileRules.IsWellFormedId).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0)
        {
            return result;
        }

        await using var command = dataSource.CreateCommand(
            $"SELECT {Columns} FROM identity.profile p WHERE p.public_profile_id = ANY(@ids) AND p.display_name IS NOT NULL AND p.locked_at IS NULL");
        command.Parameters.AddWithValue("ids", ids);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var facts = Read(reader);
            result[facts.ProfileId] = facts;
        }

        return result;
    }

    public async Task<(ProfileIdState State, PublicProfileFacts? Profile)> FindAsync(string profileId, CancellationToken cancellationToken)
    {
        if (!ProfileRules.IsWellFormedId(profileId))
        {
            return (ProfileIdState.Unknown, null);
        }

        await using (var command = dataSource.CreateCommand(
            $"SELECT {Columns} FROM identity.profile p WHERE p.public_profile_id = @id AND p.display_name IS NOT NULL AND p.locked_at IS NULL"))
        {
            command.Parameters.AddWithValue("id", profileId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return (ProfileIdState.Public, Read(reader));
            }
        }

        // Design 8.3: a locked account's profile answers as one switched off (gone), whatever its id.
        await using var retired = dataSource.CreateCommand(
            """
            SELECT EXISTS (SELECT 1 FROM identity.retired_profile_id WHERE id = @id)
                OR EXISTS (SELECT 1 FROM identity.profile p WHERE p.public_profile_id = @id AND p.locked_at IS NOT NULL)
            """);
        retired.Parameters.AddWithValue("id", profileId);
        return (await retired.ExecuteScalarAsync(cancellationToken) is true ? ProfileIdState.Retired : ProfileIdState.Unknown, null);
    }

    public async Task<string?> ShownPartyAsync(string profileId, CancellationToken cancellationToken)
    {
        if (!ProfileRules.IsWellFormedId(profileId))
        {
            return null;
        }

        // Design 8.25.9: the party already on "Profilis", shown only while the profile is public and the box's consent is in force.
        await using var command = dataSource.CreateCommand(
            """
            SELECT s.party FROM identity.profile p JOIN identity.party_support s ON s.user_id = p.user_id
            WHERE p.public_profile_id = @id AND p.display_name IS NOT NULL AND p.locked_at IS NULL
              AND EXISTS (SELECT 1 FROM identity.consent c WHERE c.user_id = p.user_id AND c.kind = @kind AND c.withdrawn_at IS NULL)
            """);
        command.Parameters.AddWithValue("id", profileId);
        command.Parameters.AddWithValue("kind", PartyPublicConsent.Kind);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static PublicProfileFacts Read(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            ProfileRules.ShownAbout(reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4)),
            reader.GetFieldValue<DateTimeOffset>(5));
}

/// <summary><see cref="IMemberProfiles"/>: the person's own form and the moderators' side, in one place for the host.</summary>
internal sealed class MemberProfiles(ProfileHandlers own, ProfileModerationHandlers moderation) : IMemberProfiles
{
    public Task<Result<MyProfileState>> MineAsync(ClaimsPrincipal user, CancellationToken cancellationToken) => own.MineAsync(user, cancellationToken);

    public Task<Result<MyProfileState>> SwitchOffAsync(ClaimsPrincipal user, string? ipHash, CancellationToken cancellationToken) =>
        own.SwitchOffOwnAsync(user, ipHash, cancellationToken);

    public Task<Result<MyProfileState>> SaveAsync(
        ClaimsPrincipal user, ProfileForm form, IReadOnlyCollection<string> parties, string? ipHash, CancellationToken cancellationToken) =>
        own.SaveAsync(user, form, parties, ipHash, cancellationToken);

    public Task<Result<Unit>> ReportAsync(Guid reporterUserId, string profileId, string ground, string? text, CancellationToken cancellationToken) =>
        moderation.ReportAsync(reporterUserId, profileId, ground, text, cancellationToken);

    public Task<IReadOnlyList<ProfileModerationItem>> QueueAsync(CancellationToken cancellationToken) => moderation.QueueAsync(cancellationToken);

    public Task<Result<Unit>> HideAboutAsync(Guid id, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken) =>
        moderation.HideAboutAsync(id, statement, moderator, contact, cancellationToken);

    public Task<Result<Unit>> KeepAsync(Guid id, string? statement, StaffActor moderator, CancellationToken cancellationToken) =>
        moderation.KeepAsync(id, statement, moderator, cancellationToken);

    public Task<Result<Unit>> RestoreAboutAsync(Guid id, string statement, StaffActor moderator, string? contact, CancellationToken cancellationToken) =>
        moderation.RestoreAboutAsync(id, statement, moderator, contact, cancellationToken);
}
