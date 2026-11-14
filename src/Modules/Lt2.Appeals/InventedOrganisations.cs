using Lt2.Appeals.Domain;
using Npgsql;

namespace Lt2.Appeals;

/// <summary>An invented organisation's statement on a bill, for local demonstration data.</summary>
public sealed record InventedStatement(string BillNumber, string Stance, string Text, string? Link, int? Goal);

/// <summary>
/// An invented organisation for local demonstration data (design 8.14.14): its profile, how it is confirmed
/// (<c>domain</c>, <c>moderator</c> or null), its one representative (an invented account with no side anywhere) and its
/// statement.
/// </summary>
public sealed record InventedOrganisationSpec(
    string Slug, string Name, string Description, string? Website, string? ConfirmedBy, Guid RepresentativeUserId, InventedStatement Statement);

/// <summary>
/// Local demonstration data only (the migrator's <c>--demo-appeal</c>, under its refusals): writes invented organisations
/// with their statements, so "Organizacijų pozicijos" can be seen. Nothing here runs in the application.
/// </summary>
public static class InventedOrganisations
{
    /// <summary>Writes each organisation that is not there yet (by its slug); returns how many were created.</summary>
    public static async Task<int> SeedAsync(string connectionString, IReadOnlyList<InventedOrganisationSpec> specs, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(specs);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var created = 0;
        var n = 0;
        foreach (var spec in specs)
        {
            n++;
            if (OrganisationRules.CheckStatement(spec.Statement.Stance, spec.Statement.Text, spec.Statement.Link, spec.Statement.Goal) is not null
                || OrganisationRules.CheckName(spec.Name) is not null)
            {
                throw new ArgumentException($"Invented organisation {spec.Slug} breaks a rule.", nameof(specs));
            }

            await using (var exists = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM appeals.organisation WHERE slug = @s)", connection, transaction))
            {
                exists.Parameters.AddWithValue("s", spec.Slug);
                if ((bool)(await exists.ExecuteScalarAsync(cancellationToken))!)
                {
                    continue;
                }
            }

            var registered = at.AddDays(-20 + n);
            var id = Guid.CreateVersion7(registered);
            var host = OrganisationRules.WebsiteHost(spec.Website);
            await using (var organisation = new NpgsqlCommand(
                """
                INSERT INTO appeals.organisation (id, slug, name, name_key, description, website, legal_code, declared_lobbyist, registered_by_user_id,
                    registered_at, status, confirmed_by, confirmed_domain, confirmed_at, confirmation_note, domain_barred, seen_at, screening_flags, changed_at)
                VALUES (@id, @slug, @name, @key, @description, @website, NULL, FALSE, @rep, @at, 'active', @by, @domain, @confirmedAt, @note, FALSE, @at, '{}', @at)
                """,
                connection,
                transaction))
            {
                organisation.Parameters.AddWithValue("id", id);
                organisation.Parameters.AddWithValue("slug", spec.Slug);
                organisation.Parameters.AddWithValue("name", spec.Name);
                organisation.Parameters.AddWithValue("key", OrganisationRules.NameKey(spec.Name));
                organisation.Parameters.AddWithValue("description", spec.Description);
                organisation.Parameters.AddWithValue("website", (object?)spec.Website ?? DBNull.Value);
                organisation.Parameters.AddWithValue("rep", spec.RepresentativeUserId);
                organisation.Parameters.AddWithValue("at", registered);
                organisation.Parameters.AddWithValue("by", (object?)spec.ConfirmedBy ?? DBNull.Value);
                organisation.Parameters.AddWithValue("domain", spec.ConfirmedBy == ConfirmationKinds.Domain ? host! : DBNull.Value);
                organisation.Parameters.AddWithValue("confirmedAt", spec.ConfirmedBy is null ? DBNull.Value : registered.AddDays(1));
                organisation.Parameters.AddWithValue("note", spec.ConfirmedBy == ConfirmationKinds.Moderator ? "Demonstraciniai duomenys." : DBNull.Value);
                await organisation.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var representative = new NpgsqlCommand(
                "INSERT INTO appeals.organisation_representative (id, organisation_id, user_id, granted_by_user_id, granted_at, ended_at) VALUES (@id, @org, @user, NULL, @at, NULL)",
                connection,
                transaction))
            {
                representative.Parameters.AddWithValue("id", Guid.CreateVersion7(registered));
                representative.Parameters.AddWithValue("org", id);
                representative.Parameters.AddWithValue("user", spec.RepresentativeUserId);
                representative.Parameters.AddWithValue("at", registered);
                await representative.ExecuteNonQueryAsync(cancellationToken);
            }

            var published = registered.AddDays(2);
            await using (var statement = new NpgsqlCommand(
                """
                INSERT INTO appeals.organisation_statement (id, organisation_id, bill_number, stance, text, link, goal, status, screening_flags, seen_at,
                    published_at, first_public_at, edited_at, withdrawn_at, written_by_user_id)
                VALUES (@id, @org, @bill, @stance, @text, @link, @goal, 'published', '{}', @at, @at, @at, NULL, NULL, @rep)
                """,
                connection,
                transaction))
            {
                statement.Parameters.AddWithValue("id", Guid.CreateVersion7(published));
                statement.Parameters.AddWithValue("org", id);
                statement.Parameters.AddWithValue("bill", spec.Statement.BillNumber);
                statement.Parameters.AddWithValue("stance", spec.Statement.Stance);
                statement.Parameters.AddWithValue("text", spec.Statement.Text);
                statement.Parameters.AddWithValue("link", (object?)spec.Statement.Link ?? DBNull.Value);
                statement.Parameters.AddWithValue("goal", (object?)spec.Statement.Goal ?? DBNull.Value);
                statement.Parameters.AddWithValue("at", published);
                statement.Parameters.AddWithValue("rep", spec.RepresentativeUserId);
                await statement.ExecuteNonQueryAsync(cancellationToken);
            }

            created++;
        }

        await transaction.CommitAsync(cancellationToken);
        return created;
    }
}
