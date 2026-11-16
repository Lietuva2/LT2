using System.Reflection;
using Lt2.Appeals;
using Lt2.Audit;
using Lt2.Identity;
using Lt2.Migrator;
using Lt2.Notifications;
using Lt2.Parliament;
using Lt2.Usage;
using Microsoft.EntityFrameworkCore;

namespace Lt2.ArchitectureTests;

/// <summary>
/// Design 6.9: the read-only database role sees only what is listed. Every table any module maps (and each module's
/// migrations history) is on the granted list or the withheld list, so a new table cannot appear without a decision.
/// </summary>
public sealed class ReadonlyRoleTests
{
    private static readonly Assembly[] ModuleAssemblies =
    [
        typeof(IdentityModule).Assembly, typeof(ParliamentModule).Assembly, typeof(AppealsModule).Assembly,
        typeof(NotificationsModule).Assembly, typeof(AuditModule).Assembly, typeof(UsageModule).Assembly,
    ];

    [Fact]
    public void Every_table_of_every_module_is_granted_to_the_read_only_role_or_withheld_from_it()
    {
        var undecided = MappedTables().Where(t => !DatabaseRoles.ReadableTables.Contains(t) && !DatabaseRoles.WithheldTables.Contains(t)).ToList();
        undecided.ShouldBeEmpty("put each new table on DatabaseRoles.ReadableTables or DatabaseRoles.WithheldTables");
    }

    [Fact]
    public void The_two_lists_name_only_tables_that_exist_and_none_twice()
    {
        var mapped = MappedTables();
        DatabaseRoles.ReadableTables.Intersect(DatabaseRoles.WithheldTables).ShouldBeEmpty();
        DatabaseRoles.ReadableTables.Concat(DatabaseRoles.WithheldTables).Where(t => !mapped.Contains(t)).ShouldBeEmpty("a listed table that no module maps");
        DatabaseRoles.ReadableTables.Concat(DatabaseRoles.WithheldTables).Select(t => t[..t.IndexOf('.', StringComparison.Ordinal)])
            .ShouldAllBe(s => DatabaseRoles.AllSchemas.Contains(s));
    }

    [Fact]
    public void Positions_marks_accounts_and_the_audit_log_are_withheld()
    {
        foreach (var table in new[] { "appeals.position", "appeals.endorsement", "appeals.writing_restriction", "identity.user", "identity.profile", "audit.event", "notify.outbox" })
        {
            DatabaseRoles.WithheldTables.ShouldContain(table);
        }

        DatabaseRoles.ReadableTables.ShouldNotContain(t => t.StartsWith("identity.", StringComparison.Ordinal) || t.StartsWith("notify.", StringComparison.Ordinal));
    }

    [Fact]
    public void The_usage_statistics_salts_and_sketches_are_withheld_and_only_their_counts_are_read()
    {
        DatabaseRoles.WithheldTables.ShouldContain("usage.sketch");
        DatabaseRoles.ReadableTables.Where(t => t.StartsWith("usage.", StringComparison.Ordinal)).Order()
            .ShouldBe(["usage.page", "usage.referrer", "usage.total"]);
    }

    /// <summary>Each module's tables as its model maps them, and its migrations history table in its first schema.</summary>
    private static HashSet<string> MappedTables()
    {
        var tables = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contextType in ModuleAssemblies.SelectMany(a => a.GetTypes()).Where(t => t.IsSubclassOf(typeof(DbContext)) && !t.IsAbstract))
        {
            using var context = CreateContext(contextType);
            var schema = context.Model.GetDefaultSchema()!;
            tables.Add(schema + ".__ef_migrations_history");
            foreach (var entity in context.Model.GetEntityTypes().Where(e => e.GetTableName() is not null))
            {
                tables.Add((entity.GetSchema() ?? schema) + "." + entity.GetTableName());
            }
        }

        return tables;
    }

    private static DbContext CreateContext(Type contextType)
    {
        if (contextType.GetMethod("StandaloneOptions", BindingFlags.NonPublic | BindingFlags.Static) is { } standalone)
        {
            return (DbContext)Activator.CreateInstance(contextType, standalone.Invoke(null, ["Host=unused"]))!;
        }

        var builderType = typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType);
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(builderType)!;
        builder.UseNpgsql("Host=unused").UseSnakeCaseNamingConvention();
        return (DbContext)Activator.CreateInstance(contextType, builder.Options)!;
    }
}