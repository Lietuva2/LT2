using System.Globalization;
using Lt2.SharedKernel;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Lt2.Migrator;

/// <summary>
/// <c>--local-code-limits</c> (design 6.2, local stack only): raises the sign-in code limits per address
/// (<c>SignIn:CodesPerHour</c>, <c>SignIn:CodesPerDay</c>) to the highest values the settings allow, so hand testing on
/// one machine does not run out of codes. Like every setting change it is written to <c>config.setting</c> and audited.
/// The defaults in code stay. It refuses unless <c>Demo:Allowed</c> is true, which only <c>deploy/.env</c> sets.
/// </summary>
public static class LocalCodeLimits
{
    public const int PerHour = 20;
    public const int PerDay = 50;

    public static async Task<int> RunAsync(IConfiguration configuration, string connectionString, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!configuration.GetValue<bool>("Demo:Allowed"))
        {
            throw new InvalidOperationException("Refused: --local-code-limits runs only where Demo:Allowed is true (the local stack's deploy/.env).");
        }

        var changed = 0;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var (key, value) in new[] { (SettingKeys.SignInCodesPerHour, PerHour), (SettingKeys.SignInCodesPerDay, PerDay) })
        {
            if (!key.IsValid(System.Text.Json.JsonSerializer.SerializeToElement(value)))
            {
                throw new InvalidOperationException($"{key.Name}: {value} is outside the setting's range.");
            }

            string? previous;
            await using (var read = new NpgsqlCommand("SELECT value::text FROM config.setting WHERE key = @k", connection, transaction))
            {
                read.Parameters.AddWithValue("k", key.Name);
                previous = (string?)await read.ExecuteScalarAsync(cancellationToken);
            }

            var text = value.ToString(CultureInfo.InvariantCulture);
            if (previous == text)
            {
                continue;
            }

            await using (var write = new NpgsqlCommand(
                """
                INSERT INTO config.setting (key, value, changed_by, changed_at) VALUES (@k, @v::jsonb, NULL, now())
                ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, changed_by = NULL, changed_at = now()
                """,
                connection,
                transaction))
            {
                write.Parameters.AddWithValue("k", key.Name);
                write.Parameters.AddWithValue("v", text);
                await write.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var audit = new NpgsqlCommand(
                """
                INSERT INTO audit.event (id, occurred_at, actor_user_id, actor_role, action, target, details, ip_hash)
                VALUES (@id, now(), NULL, 'migrator', 'setting.changed', @k, jsonb_build_object('from', @from::jsonb, 'to', @to::jsonb, 'by', 'migrator --local-code-limits'), NULL)
                """,
                connection,
                transaction))
            {
                audit.Parameters.AddWithValue("id", Guid.CreateVersion7());
                audit.Parameters.AddWithValue("k", key.Name);
                audit.Parameters.AddWithValue("from", previous ?? key.DefaultValue.GetRawText());
                audit.Parameters.AddWithValue("to", text);
                await audit.ExecuteNonQueryAsync(cancellationToken);
            }

            changed++;
        }

        await transaction.CommitAsync(cancellationToken);
        return changed;
    }
}
