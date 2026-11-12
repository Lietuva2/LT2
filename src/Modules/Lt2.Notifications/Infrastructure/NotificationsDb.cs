using Lt2.Notifications.Outbox;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Lt2.Notifications.Infrastructure;

/// <summary>The Notifications module's tables, all in schema <c>notify</c>.</summary>
internal sealed class NotificationsDb(DbContextOptions<NotificationsDb> options) : DbContext(options)
{
    public DbSet<OutboxRow> Outbox => Set<OutboxRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(NotificationsModule.Schema);
        modelBuilder.Entity<OutboxRow>(b =>
        {
            b.ToTable("outbox");
            b.HasKey(x => x.Id);
            b.Property(x => x.Kind).HasMaxLength(100);
            b.Property(x => x.Payload).HasColumnType("jsonb");
            b.HasIndex(x => x.DueAt).HasFilter("failed_at IS NULL");
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
internal sealed class NotificationsDbDesignTimeFactory : IDesignTimeDbContextFactory<NotificationsDb>
{
    public NotificationsDb CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<NotificationsDb>();
        builder.UseLt2Npgsql("Host=localhost;Database=lt2_design", NotificationsModule.Schema);
        return new NotificationsDb(builder.Options);
    }
}

internal sealed class NotificationsModuleDatabase : IModuleDatabase
{
    public string Module => "Notifications";

    public IReadOnlyList<string> Schemas => [NotificationsModule.Schema];

    public async Task MigrateAsync(string connectionString, CancellationToken cancellationToken)
    {
        var builder = new DbContextOptionsBuilder<NotificationsDb>();
        builder.UseLt2Npgsql(connectionString, NotificationsModule.Schema);
        await using var db = new NotificationsDb(builder.Options);
        await db.Database.MigrateAsync(cancellationToken);
    }
}
