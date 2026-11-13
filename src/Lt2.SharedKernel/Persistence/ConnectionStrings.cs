using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Lt2.SharedKernel.Persistence;

/// <summary>
/// Builds PostgreSQL connection strings from configuration. The connection string itself carries no password; the
/// password comes from a separate key that is read from a mounted secret file in containers (design 6.5).
/// </summary>
public static class ConnectionStrings
{
    /// <summary>
    /// The pool size when a host does not say: small, so a forgotten setting can never take PostgreSQL's connections from
    /// the other parts (design 9.5). Each host sets its own <c>Database:MaxPoolSize</c> in appsettings.json.
    /// </summary>
    public const int DefaultMaxPoolSize = 10;

    /// <summary>
    /// Reads <c>ConnectionStrings:{name}</c> and, when <paramref name="passwordKey"/> has a value, sets it as the password.
    /// The pool size is <c>Database:MaxPoolSize</c> (else <see cref="DefaultMaxPoolSize"/>, never Npgsql's 100), and
    /// <c>Database:ApplicationName</c> names the connections in <c>pg_stat_activity</c>.
    /// </summary>
    public static string Build(IConfiguration configuration, string name = "Lt2", string passwordKey = "Database:Password")
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var raw = configuration.GetConnectionString(name)
            ?? throw new InvalidOperationException($"ConnectionStrings:{name} is not configured.");
        var builder = new NpgsqlConnectionStringBuilder(raw);
        var password = configuration[passwordKey];
        if (!string.IsNullOrEmpty(password))
        {
            builder.Password = password.Trim();
        }

        builder.MaxPoolSize = configuration["Database:MaxPoolSize"] is { Length: > 0 } size
            ? int.Parse(size, System.Globalization.CultureInfo.InvariantCulture)
            : DefaultMaxPoolSize;
        if (configuration["Database:ApplicationName"] is { Length: > 0 } application && string.IsNullOrEmpty(builder.ApplicationName))
        {
            builder.ApplicationName = application;
        }

        return builder.ConnectionString;
    }
}
