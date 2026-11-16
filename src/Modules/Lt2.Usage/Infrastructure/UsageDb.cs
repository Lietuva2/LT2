using Lt2.SharedKernel;
using Lt2.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lt2.Usage.Infrastructure;

/// <summary>
/// A row of <c>usage.sketch</c> (design 8.27): the salt and the sketch of a period that has not ended, one per period and
/// audience. Deleted, salt and all, once its count is written and the period is over. Withheld from <c>lt2_readonly</c>.
/// </summary>
internal sealed class SketchRow
{
    /// <summary><c>day</c>, <c>month</c> or <c>year</c>.</summary>
    public string Period { get; set; } = "";

    /// <summary>The period's first Vilnius day.</summary>
    public DateOnly Starts { get; set; }

    /// <summary><c>visitor</c> or <c>member</c>.</summary>
    public string Audience { get; set; } = "";

    public byte[] Salt { get; set; } = [];

    public byte[] Registers { get; set; } = [];
}

/// <summary>A row of <c>usage.total</c>: the counts of one day, month or year. Kept; it holds nothing about anyone.</summary>
internal sealed class TotalRow
{
    public string Period { get; set; } = "";

    public DateOnly Starts { get; set; }

    /// <summary>Distinct visitors without a signed-in session (approximate).</summary>
    public int Visitors { get; set; }

    /// <summary>Distinct accounts with a page in a signed-in session (approximate).</summary>
    public int Members { get; set; }

    public int VisitorViews { get; set; }

    public int MemberViews { get; set; }

    /// <summary>Pages asked for by robots and tools, counted apart and in no other number.</summary>
    public int CrawlerViews { get; set; }
}

/// <summary>A row of <c>usage.page</c>: how often a path was viewed on a Vilnius day.</summary>
internal sealed class PageRow
{
    public DateOnly Day { get; set; }

    public string Path { get; set; } = "";

    public int Views { get; set; }
}

/// <summary>A row of <c>usage.referrer</c>: how many page views on a Vilnius day came from a link on another site.</summary>
internal sealed class ReferrerRow
{
    public DateOnly Day { get; set; }

    public string Host { get; set; } = "";

    public int Views { get; set; }
}

/// <summary>The Usage module's tables (schema <c>usage</c>).</summary>
internal sealed class UsageDb(DbContextOptions<UsageDb> options) : DbContext(options)
{
    public DbSet<SketchRow> Sketches => Set<SketchRow>();

    public DbSet<TotalRow> Totals => Set<TotalRow>();

    public DbSet<PageRow> Pages => Set<PageRow>();

    public DbSet<ReferrerRow> Referrers => Set<ReferrerRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(UsageModule.Schema);
        modelBuilder.Entity<SketchRow>(b =>
        {
            b.ToTable("sketch");
            b.HasKey(x => new { x.Period, x.Starts, x.Audience });
            b.Property(x => x.Period).HasMaxLength(10);
            b.Property(x => x.Audience).HasMaxLength(10);
        });
        modelBuilder.Entity<TotalRow>(b =>
        {
            b.ToTable("total");
            b.HasKey(x => new { x.Period, x.Starts });
            b.Property(x => x.Period).HasMaxLength(10);
        });
        modelBuilder.Entity<PageRow>(b =>
        {
            b.ToTable("page");
            b.HasKey(x => new { x.Day, x.Path });
            b.Property(x => x.Path).HasMaxLength(300);
        });
        modelBuilder.Entity<ReferrerRow>(b =>
        {
            b.ToTable("referrer");
            b.HasKey(x => new { x.Day, x.Host });
            b.Property(x => x.Host).HasMaxLength(253);
        });
    }
}

/// <summary>Used by <c>dotnet ef</c> only.</summary>
internal sealed class UsageDbDesignTimeFactory : IDesignTimeDbContextFactory<UsageDb>
{
    public UsageDb CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<UsageDb>();
        builder.UseLt2Npgsql("Host=localhost;Database=lt2_design", UsageModule.Schema);
        return new UsageDb(builder.Options);
    }
}

internal sealed class UsageModuleDatabase : IModuleDatabase
{
    public string Module => "Usage";

    public IReadOnlyList<string> Schemas => [UsageModule.Schema];

    public async Task MigrateAsync(string connectionString, CancellationToken cancellationToken)
    {
        var builder = new DbContextOptionsBuilder<UsageDb>();
        builder.UseLt2Npgsql(connectionString, UsageModule.Schema);
        await using var db = new UsageDb(builder.Options);
        await db.Database.MigrateAsync(cancellationToken);
    }
}
