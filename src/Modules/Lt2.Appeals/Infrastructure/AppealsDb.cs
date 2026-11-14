using Lt2.Appeals.Domain;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lt2.Appeals.Infrastructure;

/// <summary>The Appeals module's tables, all in schema <c>appeals</c> (design 5.3).</summary>
internal sealed class AppealsDb(DbContextOptions<AppealsDb> options) : DbContext(options)
{
    public DbSet<Appeal> Appeals => Set<Appeal>();

    public DbSet<Position> Positions => Set<Position>();

    public DbSet<Reason> Reasons => Set<Reason>();

    public DbSet<Endorsement> Endorsements => Set<Endorsement>();

    public DbSet<Report> Reports => Set<Report>();

    public DbSet<ModerationAction> ModerationActions => Set<ModerationAction>();

    public DbSet<Revision> Revisions => Set<Revision>();

    public DbSet<DailyStat> DailyStats => Set<DailyStat>();

    public DbSet<Flag> Flags => Set<Flag>();

    public DbSet<WritingRestriction> WritingRestrictions => Set<WritingRestriction>();


    public DbSet<AiDraftRequest> AiDraftRequests => Set<AiDraftRequest>();

    public DbSet<SummaryAttempt> SummaryAttempts => Set<SummaryAttempt>();

    public DbSet<Organisation> Organisations => Set<Organisation>();

    public DbSet<OrganisationRepresentative> OrganisationRepresentatives => Set<OrganisationRepresentative>();

    public DbSet<OrganisationInvite> OrganisationInvites => Set<OrganisationInvite>();

    public DbSet<OrganisationCode> OrganisationCodes => Set<OrganisationCode>();

    public DbSet<OrganisationStatement> OrganisationStatements => Set<OrganisationStatement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(AppealsModule.Schema);
        OnOrganisationsCreating(modelBuilder);

        modelBuilder.Entity<Appeal>(e =>
        {
            e.ToTable("appeal");
            e.HasKey(a => a.Id);
            e.Property(a => a.BillNumber).HasMaxLength(40);
            e.Property(a => a.Description).HasMaxLength(AppealRules.DescriptionMaxLength);
            e.Property(a => a.DescriptionSource).HasMaxLength(20);
            e.Property(a => a.DescriptionUrl).HasMaxLength(500);
            e.Property(a => a.Status).HasMaxLength(20);
            e.Property(a => a.Sending).HasMaxLength(20);
            e.Property(a => a.ThresholdSetBy).HasMaxLength(20);
            e.Property(a => a.ThresholdNote).HasMaxLength(1000);

            // One record per bill (design 8.12.1): two people arriving together make one row.
            e.HasIndex(a => a.BillNumber).IsUnique().HasFilter("status IN ('open', 'closed', 'rejected')").HasDatabaseName("ix_appeal_bill_number_record");
            e.HasIndex(a => a.Status);
            e.ToTable(t => t.HasCheckConstraint("ck_appeal_threshold_positive", "threshold > 0"));
        });

        modelBuilder.Entity<Position>(e =>
        {
            e.ToTable("position");
            e.HasKey(p => new { p.AppealId, p.UserId });
            e.Property(p => p.Side).HasMaxLength(10);
            e.HasIndex(p => p.UserId);
            e.HasOne<Appeal>().WithMany().HasForeignKey(p => p.AppealId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("ck_position_side", "side IN ('for', 'against')"));
        });

        modelBuilder.Entity<Reason>(e =>
        {
            e.ToTable("reason");
            e.HasKey(r => r.Id);
            e.Property(r => r.Side).HasMaxLength(10);
            e.Property(r => r.Text).HasMaxLength(AppealRules.ReasonMaxLength);
            e.Property(r => r.Status).HasMaxLength(20);
            e.HasIndex(r => new { r.AppealId, r.Status });
            e.HasIndex(r => new { r.AppealId, r.AuthorUserId });
            e.HasOne<Appeal>().WithMany().HasForeignKey(r => r.AppealId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Reason>().WithMany().HasForeignKey(r => r.MergedIntoId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("ck_reason_side", "side IN ('for', 'against')"));
        });

        modelBuilder.Entity<Endorsement>(e =>
        {
            e.ToTable("endorsement");
            e.HasKey(x => new { x.ReasonId, x.UserId });
            e.HasIndex(x => new { x.AppealId, x.UserId });
            e.HasOne<Reason>().WithMany().HasForeignKey(x => x.ReasonId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Appeal>().WithMany().HasForeignKey(x => x.AppealId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Report>(e =>
        {
            e.ToTable("report");
            e.HasKey(r => r.Id);
            e.Property(r => r.TargetKind).HasMaxLength(20);
            e.Property(r => r.Ground).HasMaxLength(40);
            e.Property(r => r.Text).HasMaxLength(1000);
            e.Property(r => r.Status).HasMaxLength(20);
            e.HasIndex(r => new { r.TargetKind, r.TargetId, r.Status });
            e.HasIndex(r => new { r.TargetId, r.ReporterUserId }).IsUnique().HasFilter("status = 'open'");
        });

        modelBuilder.Entity<WritingRestriction>(e =>
        {
            e.ToTable("writing_restriction");
            e.HasKey(w => w.Id);
            e.Property(w => w.Statement).HasMaxLength(1000);
            e.Property(w => w.LiftStatement).HasMaxLength(1000);
            e.HasIndex(w => w.UserId);
        });

        modelBuilder.Entity<ModerationAction>(e =>
        {
            e.ToTable("moderation_action");
            e.HasKey(m => m.Id);
            e.Property(m => m.TargetKind).HasMaxLength(20);
            e.Property(m => m.Action).HasMaxLength(40);
            e.Property(m => m.Statement).HasMaxLength(1000);
            e.HasIndex(m => new { m.TargetKind, m.TargetId });
        });

        modelBuilder.Entity<Revision>(e =>
        {
            e.ToTable("revision");
            e.HasKey(r => r.Id);
            e.Property(r => r.EntityKind).HasMaxLength(20);
            e.Property(r => r.Field).HasMaxLength(40);
            e.HasIndex(r => new { r.EntityKind, r.EntityId });
        });

        modelBuilder.Entity<Flag>(e =>
        {
            e.ToTable("flag");
            e.HasKey(f => f.Id);
            e.Property(f => f.Kind).HasMaxLength(20);
            e.Property(f => f.Link).HasMaxLength(500);
            e.Property(f => f.Note).HasMaxLength(1000);
            e.Property(f => f.Status).HasMaxLength(20);
            e.HasIndex(f => new { f.AppealId, f.Status });
            e.HasIndex(f => new { f.AppealId, f.ReporterUserId }).IsUnique().HasFilter("status = 'open'");
            e.HasOne<Appeal>().WithMany().HasForeignKey(f => f.AppealId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AiDraftRequest>(e =>
        {
            e.ToTable("ai_draft_request");
            e.HasKey(r => r.Id);
            e.Property(r => r.BillNumber).HasMaxLength(40);
            e.HasIndex(r => new { r.ModeratorUserId, r.RequestedAt });
        });

        modelBuilder.Entity<SummaryAttempt>(e =>
        {
            e.ToTable("summary_attempt");
            e.HasKey(s => s.BillNumber);
            e.Property(s => s.BillNumber).HasMaxLength(40);
            e.Property(s => s.Outcome).HasMaxLength(20);
            e.HasIndex(s => s.TriedAt);
        });

        modelBuilder.Entity<DailyStat>(e =>
        {
            e.ToTable("daily_stat");
            e.HasKey(d => new { d.AppealId, d.Day });
            e.Property(d => d.NewAccountShare).HasPrecision(5, 4);
            e.HasOne<Appeal>().WithMany().HasForeignKey(d => d.AppealId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    /// <summary>Organisations and their statements (design 8.14.10, 8.14.14 stage A: tables of this module).</summary>
    private static void OnOrganisationsCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Organisation>(e =>
        {
            e.ToTable("organisation");
            e.HasKey(o => o.Id);
            e.Property(o => o.Slug).HasMaxLength(OrganisationRules.SlugMaxLength + 8);
            e.Property(o => o.Name).HasMaxLength(OrganisationRules.NameMaxLength);
            e.Property(o => o.NameKey).HasMaxLength(OrganisationRules.NameMaxLength);
            e.Property(o => o.Description).HasMaxLength(OrganisationRules.DescriptionMaxLength);
            e.Property(o => o.Website).HasMaxLength(OrganisationRules.WebsiteMaxLength);
            e.Property(o => o.LegalCode).HasMaxLength(9);
            e.Property(o => o.Status).HasMaxLength(20);
            e.Property(o => o.ConfirmedBy).HasMaxLength(20);
            e.Property(o => o.ConfirmedDomain).HasMaxLength(253);
            e.Property(o => o.ConfirmationNote).HasMaxLength(1000);
            e.Property(o => o.BlockStatement).HasMaxLength(1000);
            e.HasIndex(o => o.Slug).IsUnique();

            // Design 8.14.14: no other organisation that is not blocked or closed has the same name, compared without case.
            e.HasIndex(o => o.NameKey).IsUnique().HasFilter("status = 'active'").HasDatabaseName("ix_organisation_name_key_active");
            e.HasIndex(o => o.RegisteredByUserId);
            e.ToTable(t => t.HasCheckConstraint("ck_organisation_status", "status IN ('active', 'blocked', 'closed')"));
            e.ToTable(t => t.HasCheckConstraint("ck_organisation_confirmed_by", "confirmed_by IS NULL OR confirmed_by IN ('domain', 'moderator')"));
        });

        modelBuilder.Entity<OrganisationRepresentative>(e =>
        {
            e.ToTable("organisation_representative");
            e.HasKey(r => r.Id);
            e.HasIndex(r => new { r.OrganisationId, r.UserId }).IsUnique().HasFilter("ended_at IS NULL").HasDatabaseName("ix_organisation_representative_current");
            e.HasIndex(r => r.UserId);
            e.HasOne<Organisation>().WithMany().HasForeignKey(r => r.OrganisationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrganisationInvite>(e =>
        {
            e.ToTable("organisation_invite");
            e.HasKey(i => i.Id);
            e.HasIndex(i => i.TokenHash).IsUnique();
            e.HasIndex(i => i.OrganisationId);
            e.HasOne<Organisation>().WithMany().HasForeignKey(i => i.OrganisationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrganisationCode>(e =>
        {
            e.ToTable("organisation_code");
            e.HasKey(c => c.Id);
            e.Property(c => c.Domain).HasMaxLength(253);
            e.HasIndex(c => new { c.OrganisationId, c.CreatedAt });
            e.HasOne<Organisation>().WithMany().HasForeignKey(c => c.OrganisationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrganisationStatement>(e =>
        {
            e.ToTable("organisation_statement");
            e.HasKey(s => s.Id);
            e.Property(s => s.BillNumber).HasMaxLength(40);
            e.Property(s => s.Stance).HasMaxLength(10);
            e.Property(s => s.Text).HasMaxLength(OrganisationRules.StatementMaxLength);
            e.Property(s => s.Link).HasMaxLength(OrganisationRules.LinkMaxLength);
            e.Property(s => s.Status).HasMaxLength(20);

            // One statement per organisation per bill (design 8.14.3); a withdrawn one makes room for a new one.
            e.HasIndex(s => new { s.OrganisationId, s.BillNumber }).IsUnique().HasFilter("status IN ('pending', 'published', 'hidden')")
                .HasDatabaseName("ix_organisation_statement_one_per_bill");
            e.HasIndex(s => new { s.BillNumber, s.Status });

            // The follow job's look at what became public lately (design 8.14.14, stage B).
            e.HasIndex(s => new { s.OrganisationId, s.FirstPublicAt });
            e.HasOne<Organisation>().WithMany().HasForeignKey(s => s.OrganisationId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("ck_organisation_statement_stance", "stance IN ('for', 'against', 'remarks')"));
            e.ToTable(t => t.HasCheckConstraint("ck_organisation_statement_goal", "goal IS NULL OR (goal BETWEEN 1 AND 1000000 AND stance <> 'remarks')"));
        });
    }

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
