using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Lt2.SharedKernel;
using Npgsql;

namespace Lt2.Audit.Infrastructure;

/// <summary>A row of <c>audit.event</c>. Append-only: the application role may only insert.</summary>
internal sealed class AuditEvent
{
    public Guid Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public Guid? ActorUserId { get; set; }

    public string? ActorRole { get; set; }

    public string Action { get; set; } = "";

    public string? Target { get; set; }

    /// <summary>JSON (<c>jsonb</c>).</summary>
    public string? Details { get; set; }

    public string? IpHash { get; set; }
}

/// <summary>A row of <c>config.setting</c>.</summary>
internal sealed class Setting
{
    public string Key { get; set; } = "";

    /// <summary>JSON (<c>jsonb</c>).</summary>
    public string Value { get; set; } = "null";

    public Guid? ChangedBy { get; set; }

    public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>The Audit module's tables: <c>audit.event</c> and <c>config.setting</c>.</summary>
internal sealed class AuditDb(DbContextOptions<AuditDb> options) : DbContext(options)
{
    public DbSet<AuditEvent> Events => Set<AuditEvent>();

    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(AuditModule.AuditSchema);
        modelBuilder.Entity<AuditEvent>(b =>
        {
            b.ToTable("event", AuditModule.AuditSchema);
            b.HasKey(x => x.Id);
            b.Property(x => x.Action).HasMaxLength(100);
            b.Property(x => x.ActorRole).HasMaxLength(50);
            b.Property(x => x.Target).HasMaxLength(200);
            b.Property(x => x.Details).HasColumnType("jsonb");
            b.Property(x => x.IpHash).HasMaxLength(64);
            b.HasIndex(x => x.OccurredAt);
        });
        modelBuilder.Entity<Setting>(b =>
        {
            b.ToTable("setting", AuditModule.ConfigSchema);
            b.HasKey(x => x.Key);
            b.Property(x => x.Key).HasMaxLength(100);
            b.Property(x => x.Value).HasColumnType("jsonb");
        });
    }
}

internal static class DbContextOptionsExtensions
{
    public static DbContextOptionsBuilder UseLt2Npgsql(this DbContextOptionsBuilder options, NpgsqlDataSource dataSource, string schema) =>
        options.UseNpgsql(dataSource, o => o.MigrationsHistoryTable("__ef_migrations_history", schema))
            .UseSnakeCaseNamingConvention();

    public static DbContextOptionsBuilder UseLt2Npgsql(this DbContextOptionsBuilder options, string connectionString, string schema) =>
        options.UseNpgsql(connectionString, o => o.MigrationsHistoryTable("__ef_migrations_history", schema))
            .UseSnakeCaseNamingConvention();
}

/// <summary>Used by <c>dotnet ef</c> only.</summary>
internal sealed class AuditDbDesignTimeFactory : IDesignTimeDbContextFactory<AuditDb>
{
    public AuditDb CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<AuditDb>();
        builder.UseLt2Npgsql("Host=localhost;Database=lt2_design", AuditModule.AuditSchema);
        return new AuditDb(builder.Options);
    }
}

internal sealed class AuditModuleDatabase : IModuleDatabase
{
    public string Module => "Audit";

    public IReadOnlyList<string> Schemas => [AuditModule.AuditSchema, AuditModule.ConfigSchema];

    public async Task MigrateAsync(string connectionString, CancellationToken cancellationToken)
    {
        var builder = new DbContextOptionsBuilder<AuditDb>();
        builder.UseLt2Npgsql(connectionString, AuditModule.AuditSchema);
        await using var db = new AuditDb(builder.Options);
        await db.Database.MigrateAsync(cancellationToken);
    }
}
