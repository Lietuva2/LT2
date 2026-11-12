using Lt2.Parliament.Domain;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lt2.Parliament.Infrastructure;

/// <summary>The Parliament module's tables, all in schema <c>parliament</c> (design 5.1).</summary>
internal sealed class ParliamentDb(DbContextOptions<ParliamentDb> options) : DbContext(options)
{
    public DbSet<Term> Terms => Set<Term>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<Sitting> Sittings => Set<Sitting>();

    public DbSet<Person> People => Set<Person>();

    public DbSet<Mandate> Mandates => Set<Mandate>();

    public DbSet<Body> Bodies => Set<Body>();

    public DbSet<Membership> Memberships => Set<Membership>();

    public DbSet<Contact> Contacts => Set<Contact>();

    public DbSet<Bill> Bills => Set<Bill>();

    public DbSet<AgendaItem> AgendaItems => Set<AgendaItem>();

    public DbSet<Vote> Votes => Set<Vote>();

    public DbSet<Ballot> Ballots => Set<Ballot>();

    public DbSet<FactionPositionRow> FactionPositions => Set<FactionPositionRow>();

    public DbSet<MpStat> MpStats => Set<MpStat>();

    public DbSet<ImportRun> ImportRuns => Set<ImportRun>();

    public DbSet<Import.Agenda.PlannedItem> PlannedItems => Set<Import.Agenda.PlannedItem>();

    public DbSet<Import.Agenda.AgendaSnapshot> AgendaSnapshots => Set<Import.Agenda.AgendaSnapshot>();

    public DbSet<RawDocument> RawDocuments => Set<RawDocument>();

    public DbSet<FetchState> FetchStates => Set<FetchState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(ParliamentModule.Schema);

        modelBuilder.Entity<Import.Agenda.PlannedItem>(e =>
        {
            e.ToTable("planned_item");
            e.HasKey(p => p.Id);
            e.Property(p => p.BillNumber).HasMaxLength(40);
            e.Property(p => p.Stage).HasMaxLength(16);
            e.Property(p => p.Source).HasMaxLength(16);
            e.Property(p => p.Note).HasMaxLength(1000);
            e.HasIndex(p => new { p.BillNumber, p.GoneAt });
        });

        modelBuilder.Entity<Import.Agenda.AgendaSnapshot>(e =>
        {
            e.ToTable("agenda_snapshot");
            e.HasKey(s => s.Id);
            e.HasIndex(s => s.FetchedAt);
        });

        modelBuilder.Entity<Term>(e =>
        {
            e.ToTable("term");
            e.Property(x => x.Id).ValueGeneratedNever();
        });
        modelBuilder.Entity<Session>(e =>
        {
            e.ToTable("session");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasOne<Term>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Sitting>(e =>
        {
            e.ToTable("sitting");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasOne<Session>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.StartsAt);
        });
        modelBuilder.Entity<Person>(e =>
        {
            e.ToTable("person");
            e.HasIndex(x => x.SeimasPersonId).IsUnique();
            e.HasIndex(x => x.Slug).IsUnique();
        });
        modelBuilder.Entity<Mandate>(e =>
        {
            e.ToTable("mandate");
            e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Term>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PersonId, x.TermId }).IsUnique();
        });
        modelBuilder.Entity<Body>(e =>
        {
            e.ToTable("body");
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
            e.HasOne<Term>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TermId, x.SeimasId }).IsUnique();
            e.HasIndex(x => new { x.TermId, x.Slug }).IsUnique();
        });
        modelBuilder.Entity<Membership>(e =>
        {
            e.ToTable("membership");
            e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Body>().WithMany().HasForeignKey(x => x.BodyId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.PersonId, x.BodyId, x.From }).IsUnique();
        });
        modelBuilder.Entity<Contact>(e =>
        {
            e.ToTable("contact");
            e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.PersonId, x.Kind, x.Value }).IsUnique();
        });
        modelBuilder.Entity<Bill>(e =>
        {
            e.ToTable("bill");
            e.Property(x => x.ESeimasUrl).HasColumnName("e_seimas_url");
            e.HasIndex(x => x.Number).IsUnique();
        });
        modelBuilder.Entity<AgendaItem>(e =>
        {
            e.ToTable("agenda_item");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasOne<Sitting>().WithMany().HasForeignKey(x => x.SittingId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Bill>().WithMany().HasForeignKey(x => x.BillId).OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<Vote>(e =>
        {
            e.ToTable("vote");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Stage).HasConversion<string>().HasMaxLength(16);
            e.HasOne<AgendaItem>().WithMany().HasForeignKey(x => x.AgendaItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Bill>().WithMany().HasForeignKey(x => x.BillId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.HeldAt);
        });
        modelBuilder.Entity<Ballot>(e =>
        {
            e.ToTable("ballot");
            e.HasKey(x => new { x.VoteId, x.PersonId });
            e.Property(x => x.Choice).HasConversion<string>().HasMaxLength(16);
            e.HasOne<Vote>().WithMany().HasForeignKey(x => x.VoteId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Body>().WithMany().HasForeignKey(x => x.FactionId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.PersonId);
        });
        modelBuilder.Entity<FactionPositionRow>(e =>
        {
            e.ToTable("faction_position");
            e.HasKey(x => new { x.VoteId, x.FactionLabel });
            e.Property(x => x.Position).HasConversion<string>().HasMaxLength(16);
            e.HasOne<Vote>().WithMany().HasForeignKey(x => x.VoteId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Body>().WithMany().HasForeignKey(x => x.FactionId).OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<MpStat>(e =>
        {
            e.ToTable("mp_stat");
            e.HasKey(x => new { x.PersonId, x.TermId });
            e.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Term>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ImportRun>(e =>
        {
            e.ToTable("import_run");
            e.Property(x => x.Status).HasMaxLength(16);
            e.HasIndex(x => x.StartedAt);
        });
        modelBuilder.Entity<RawDocument>(e =>
        {
            e.ToTable("raw_document");
            e.HasIndex(x => new { x.Request, x.Sha256 }).IsUnique();
            e.HasIndex(x => x.Synthetic).HasFilter("synthetic");
        });
        modelBuilder.Entity<FetchState>(e =>
        {
            e.ToTable("fetch_state");
            e.HasKey(x => x.Request);
        });
    }

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
