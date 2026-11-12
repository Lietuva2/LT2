using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lt2.Appeals.Infrastructure;

/// <summary>The Appeals module's tables, all in schema <c>appeals</c>. Empty in M0.</summary>
internal sealed class AppealsDb(DbContextOptions<AppealsDb> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.HasDefaultSchema(AppealsModule.Schema);

    internal static DbContextOptions<AppealsDb> Options(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<AppealsDb>();
        builder.UseNpgsql(connectionString, o => o.MigrationsHistoryTable("__ef_migrations_history", AppealsModule.Schema))
            .UseSnakeCaseNamingConvention();
        return builder.Options;
    }
}

/// <summary>Used by <c>dotnet ef</c> only.</summary>
internal sealed class AppealsDbDesignTimeFactory : IDesignTimeDbContextFactory<AppealsDb>
{
    public AppealsDb CreateDbContext(string[] args) => new(AppealsDb.Options("Host=localhost;Database=lt2_design"));
}

internal sealed class AppealsModuleDatabase : IModuleDatabase
{
    public string Module => "Appeals";

    public IReadOnlyList<string> Schemas => [AppealsModule.Schema];

    public async Task MigrateAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var db = new AppealsDb(AppealsDb.Options(connectionString));
        await db.Database.MigrateAsync(cancellationToken);
    }
}
