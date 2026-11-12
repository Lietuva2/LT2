using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lt2.Parliament.Infrastructure;

/// <summary>The Parliament module's tables, all in schema <c>parliament</c>. Empty in M0.</summary>
internal sealed class ParliamentDb(DbContextOptions<ParliamentDb> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.HasDefaultSchema(ParliamentModule.Schema);

    internal static DbContextOptions<ParliamentDb> Options(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<ParliamentDb>();
        builder.UseNpgsql(connectionString, o => o.MigrationsHistoryTable("__ef_migrations_history", ParliamentModule.Schema))
            .UseSnakeCaseNamingConvention();
        return builder.Options;
    }
}

/// <summary>Used by <c>dotnet ef</c> only.</summary>
internal sealed class ParliamentDbDesignTimeFactory : IDesignTimeDbContextFactory<ParliamentDb>
{
    public ParliamentDb CreateDbContext(string[] args) => new(ParliamentDb.Options("Host=localhost;Database=lt2_design"));
}

internal sealed class ParliamentModuleDatabase : IModuleDatabase
{
    public string Module => "Parliament";

    public IReadOnlyList<string> Schemas => [ParliamentModule.Schema];

    public async Task MigrateAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var db = new ParliamentDb(ParliamentDb.Options(connectionString));
        await db.Database.MigrateAsync(cancellationToken);
    }
}
