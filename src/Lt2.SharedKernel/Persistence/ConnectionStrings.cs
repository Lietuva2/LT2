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
    /// Reads <c>ConnectionStrings:{name}</c> and, when <paramref name="passwordKey"/> has a value, sets it as the password.
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

        return builder.ConnectionString;
    }
}
