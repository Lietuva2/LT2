using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lt2.SharedKernel.Persistence;

/// <summary>
/// The one way a module's <c>DbContext</c> is pointed at PostgreSQL: Npgsql, the migrations history table in the module's
/// own schema, and snake_case names (AGENTS.md: one <c>DbContext</c> and one schema per module).
/// </summary>
public static class Lt2Npgsql
{
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    /// <summary>On the host's shared data source (the web host and the worker).</summary>
    public static DbContextOptionsBuilder UseLt2Npgsql(this DbContextOptionsBuilder options, NpgsqlDataSource dataSource, string schema) =>
        options.UseNpgsql(dataSource, o => o.MigrationsHistoryTable(MigrationsHistoryTable, schema)).UseSnakeCaseNamingConvention();

    /// <summary>On a connection the caller owns (another module's transaction, as when an account is deleted).</summary>
    public static DbContextOptionsBuilder UseLt2Npgsql(this DbContextOptionsBuilder options, System.Data.Common.DbConnection connection, string schema) =>
        options.UseNpgsql(connection, o => o.MigrationsHistoryTable(MigrationsHistoryTable, schema)).UseSnakeCaseNamingConvention();

    /// <summary>On a connection string (the migrator and <c>dotnet ef</c>).</summary>
    public static DbContextOptionsBuilder UseLt2Npgsql(this DbContextOptionsBuilder options, string connectionString, string schema) =>
        options.UseNpgsql(connectionString, o => o.MigrationsHistoryTable(MigrationsHistoryTable, schema)).UseSnakeCaseNamingConvention();
}
