using Lt2.Appeals.Domain;
using Lt2.SharedKernel;
using Lt2.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lt2.Appeals.Infrastructure;

/// <summary>The Appeals module's tables, all in schema <c>appeals</c> (design 5.3).</summary>
internal sealed class AppealsDb(DbContextOptions<AppealsDb> options) : DbContext(options)
{
    public DbSet<BillRecord> BillRecords => Set<BillRecord>();

    public DbSet<BillCard> BillCards => Set<BillCard>();

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

    public DbSet<MpResponse> MpResponses => Set<MpResponse>();

    public DbSet<Comment> Comments => Set<Comment>();

    public DbSet<CommentAgreement> CommentAgreements => Set<CommentAgreement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(AppealsModule.Schema);
        OnOrganisationsCreating(modelBuilder);
        OnMpResponsesCreating(modelBuilder);
        OnCommentsCreating(modelBuilder);

        modelBuilder.Entity<BillRecord>(e =>
        {
            e.ToTable("bill_record");
            e.HasKey(a => a.Id);
            e.Property(a => a.BillNumber).HasMaxLength(40);
            e.Property(a => a.Description).HasMaxLength(AppealRules.DescriptionMaxLength);
            e.Property(a => a.PlainTitle).HasMaxLength(AppealRules.PlainTitleMaxLength);
            e.Property(a => a.DescriptionSource).HasMaxLength(20);
            e.Property(a => a.DescriptionUrl).HasMaxLength(500);
            e.Property(a => a.Status).HasMaxLength(20);
            e.Property(a => a.Sending).HasMaxLength(20);
            e.Property(a => a.ThresholdSetBy).HasMaxLength(20);
            e.Property(a => a.ThresholdNote).HasMaxLength(1000);
            e.Property(a => a.ClosedReason).HasMaxLength(20);
            e.Property(a => a.ClosedSourceUrl).HasMaxLength(RecordRules.ClosingUrlMaxLength);

            // One record per bill (design 8.12.1): two people arriving together make one row.
            e.HasIndex(a => a.BillNumber).IsUnique().HasFilter("status IN ('open', 'closed', 'rejected')").HasDatabaseName("ix_bill_record_bill_number");
            e.HasIndex(a => a.Status);
            e.ToTable(t => t.HasCheckConstraint("ck_bill_record_threshold_positive", "threshold > 0"));
        });

        // The search copy of the bill catalogue (design 8.12.5): the folded texts are written with the row, the two search columns
        // are generated from them, and the indexes serve the text search and the part-of-a-word search.
        modelBuilder.Entity<BillCard>(e =>
        {
            e.ToTable("bill_card");
            e.HasKey(c => c.BillNumber);
            e.Property(c => c.BillNumber).HasColumnType("text");
            e.Property(c => c.Title).HasColumnType("text");
            e.Property(c => c.PlainTitle).HasMaxLength(AppealRules.PlainTitleMaxLength);
            e.Property(c => c.TitleFolded).HasColumnType("text");
            e.Property(c => c.PlainTitleFolded).HasColumnType("text");
            e.Property(c => c.SearchFolded).HasColumnType("text").HasComputedColumnSql("title_folded || ' ' || coalesce(plain_title_folded, '')", stored: true);
            e.Property(c => c.SearchVector).HasColumnType("tsvector")
                .HasComputedColumnSql("to_tsvector('lithuanian'::regconfig, bill_number || ' ' || title || ' ' || coalesce(plain_title, ''))", stored: true);
            e.Property(c => c.SearchVectorFolded).HasColumnType("tsvector")
                .HasComputedColumnSql("to_tsvector('lithuanian'::regconfig, title_folded || ' ' || coalesce(plain_title_folded, ''))", stored: true);
            e.HasIndex(c => c.SearchVector).HasMethod("gin").HasDatabaseName("ix_bill_card_search_vector");
            e.HasIndex(c => c.SearchVectorFolded).HasMethod("gin").HasDatabaseName("ix_bill_card_search_vector_folded");
            e.HasIndex(c => c.SearchFolded).HasMethod("gin").HasOperators("appeals.gin_trgm_ops").HasDatabaseName("ix_bill_card_search_folded");
        });

        modelBuilder.Entity<Position>(e =>
        {
            e.ToTable("position");
            e.HasKey(p => new { p.RecordId, p.UserId });
            e.Property(p => p.Side).HasMaxLength(10);
            e.HasIndex(p => p.UserId);
            e.HasOne<BillRecord>().WithMany().HasForeignKey(p => p.RecordId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("ck_position_side", "side IN ('for', 'against')"));
        });

        modelBuilder.Entity<Reason>(e =>
        {
            e.ToTable("reason");
            e.HasKey(r => r.Id);
            e.Property(r => r.Side).HasMaxLength(10);
            // Design 8.2 (owner, 2026-11-16): the length is a setting, checked where an argument is written; the column holds any.
            e.Property(r => r.Text).HasColumnType("text");
            e.Property(r => r.Status).HasMaxLength(20);
            e.HasIndex(r => new { r.RecordId, r.Status });
            e.HasIndex(r => new { r.RecordId, r.AuthorUserId });
            e.HasOne<BillRecord>().WithMany().HasForeignKey(r => r.RecordId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Reason>().WithMany().HasForeignKey(r => r.MergedIntoId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("ck_reason_side", "side IN ('for', 'against')"));
        });

        modelBuilder.Entity<Endorsement>(e =>
        {
            e.ToTable("endorsement");
            e.HasKey(x => new { x.ReasonId, x.UserId });
            e.HasIndex(x => new { x.RecordId, x.UserId });
            e.HasOne<Reason>().WithMany().HasForeignKey(x => x.ReasonId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<BillRecord>().WithMany().HasForeignKey(x => x.RecordId).OnDelete(DeleteBehavior.Cascade);
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
            e.HasIndex(f => new { f.RecordId, f.Status });
            e.HasIndex(f => new { f.RecordId, f.ReporterUserId }).IsUnique().HasFilter("status = 'open'");
            e.HasOne<BillRecord>().WithMany().HasForeignKey(f => f.RecordId).OnDelete(DeleteBehavior.Cascade);
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
            e.HasKey(d => new { d.RecordId, d.Day });
            e.Property(d => d.NewAccountShare).HasPrecision(5, 4);
            e.HasOne<BillRecord>().WithMany().HasForeignKey(d => d.RecordId).OnDelete(DeleteBehavior.Cascade);
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
            e.Property(o => o.KeptOutStatement).HasMaxLength(1000);
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
            e.Property(s => s.Text).HasColumnType("text"); // Texts:MaxLength (owner, 2026-11-16), checked where it is written
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

    /// <summary>MPs' responses on bills (design 5.3, 8.5.5): one per MP per bill; withheld from the read-only role.</summary>
    private static void OnMpResponsesCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<MpResponse>(e =>
        {
            e.ToTable("mp_response");
            e.HasKey(r => r.Id);
            e.Property(r => r.BillNumber).HasMaxLength(40);
            e.Property(r => r.Stance).HasMaxLength(10);
            e.Property(r => r.Text).HasColumnType("text"); // Texts:MaxLength (owner, 2026-11-16), checked where it is written
            e.Property(r => r.Status).HasMaxLength(20);
            e.Property(r => r.HideStatement).HasMaxLength(1000);

            // One per MP per bill, whatever its status: one written again after a withdrawal is the same response (design 8.5.5).
            e.HasIndex(r => new { r.SeimasPersonId, r.BillNumber }).IsUnique().HasDatabaseName("ix_mp_response_one_per_bill");
            e.HasIndex(r => r.BillNumber);
            e.HasIndex(r => r.WrittenByUserId);

            // The followers' daily email (design 8.20.1) reads one Vilnius day of announced stances.
            e.HasIndex(r => r.AnnouncedAt);
            e.ToTable(t => t.HasCheckConstraint("ck_mp_response_stance", "stance IN ('for', 'against', 'undecided')"));
            e.ToTable(t => t.HasCheckConstraint("ck_mp_response_status", "status IN ('published', 'withdrawn', 'hidden')"));
        });

    /// <summary>Comments and their "Sutinku" (design 8.23.11): <c>comment</c> withheld from the read-only role, <c>comment_agreement</c> readable.</summary>
    private static void OnCommentsCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Comment>(e =>
        {
            e.ToTable("comment");
            e.HasKey(c => c.Id);
            e.Property(c => c.TargetKind).HasMaxLength(20);
            e.Property(c => c.BillNumber).HasMaxLength(40);
            e.Property(c => c.SpeaksAs).HasMaxLength(20);
            e.Property(c => c.Text).HasColumnType("text"); // Texts:MaxLength (owner, 2026-11-16), checked where it is written
            e.Property(c => c.Status).HasMaxLength(20);
            e.HasIndex(c => new { c.TargetKind, c.TargetId, c.CreatedAt });
            e.HasIndex(c => c.BillNumber);
            e.HasIndex(c => c.AuthorUserId);

            // The author's notice (design 8.23.9) reads the comments written since the follow job's last run.
            e.HasIndex(c => c.CreatedAt);
            e.ToTable(t => t.HasCheckConstraint("ck_comment_target_kind", "target_kind IN ('reason', 'statement', 'response')"));
            e.ToTable(t => t.HasCheckConstraint("ck_comment_status", "status IN ('published', 'hidden')"));
            e.ToTable(t => t.HasCheckConstraint(
                "ck_comment_voice",
                "(speaks_as = 'person' AND organisation_id IS NULL AND seimas_person_id IS NULL) OR (speaks_as = 'organisation' AND organisation_id IS NOT NULL AND target_kind = 'statement') OR (speaks_as = 'mp' AND seimas_person_id IS NOT NULL AND target_kind = 'response')"));
        });

        modelBuilder.Entity<CommentAgreement>(e =>
        {
            e.ToTable("comment_agreement");
            e.HasKey(a => new { a.CommentId, a.UserId });
            e.HasIndex(a => a.UserId);
            e.HasOne<Comment>().WithMany().HasForeignKey(a => a.CommentId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    internal static DbContextOptions<AppealsDb> Options(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<AppealsDb>();
        builder.UseLt2Npgsql(connectionString, AppealsModule.Schema);
        return builder.Options;
    }

    /// <summary>Options on a connection the caller owns (another module's transaction, as when an account is deleted).</summary>
    internal static DbContextOptions<AppealsDb> Options(System.Data.Common.DbConnection connection)
    {
        var builder = new DbContextOptionsBuilder<AppealsDb>();
        builder.UseLt2Npgsql(connection, AppealsModule.Schema);
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
