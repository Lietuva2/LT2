using Lt2.SharedKernel;

namespace Lt2.Identity.Domain;

/// <summary>
/// The consent to store positions that counts (design 6.5): given to the text version in force and not withdrawn. The one
/// definition, as a query and as the SQL the participant directory runs (alias <c>p</c> for the profile).
/// </summary>
internal static class CurrentPositionConsent
{
    /// <summary>Parameters <c>@consent_kind</c> and <c>@consent_version</c>; see <see cref="AddParameters"/>.</summary>
    public const string ExistsSql =
        "EXISTS (SELECT 1 FROM identity.consent c WHERE c.user_id = p.user_id AND c.kind = @consent_kind AND c.text_version = @consent_version AND c.withdrawn_at IS NULL)";

    public static IQueryable<Consent> Of(IQueryable<Consent> consents, Guid userId) =>
        consents.Where(c => c.UserId == userId && c.Kind == PositionConsent.Kind && c.TextVersion == PositionConsent.TextVersion && c.WithdrawnAt == null);

    public static void AddParameters(Npgsql.NpgsqlCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.Parameters.AddWithValue("consent_kind", PositionConsent.Kind);
        command.Parameters.AddWithValue("consent_version", PositionConsent.TextVersion);
    }
}
