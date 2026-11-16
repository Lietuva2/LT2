using Lt2.Notifications.Outbox;
using Lt2.SharedKernel;
using Lt2.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lt2.Notifications.Infrastructure;

/// <summary>The Notifications module's tables, all in schema <c>notify</c>.</summary>
internal sealed class NotificationsDb(DbContextOptions<NotificationsDb> options) : DbContext(options)
{
    public DbSet<OutboxRow> Outbox => Set<OutboxRow>();

    public DbSet<Digests.DigestRun> DigestRuns => Set<Digests.DigestRun>();

    public DbSet<Digests.DigestItem> DigestItems => Set<Digests.DigestItem>();

    public DbSet<Digests.DigestDelivery> DigestDeliveries => Set<Digests.DigestDelivery>();

    public DbSet<Digests.MpEmailPref> MpEmailPrefs => Set<Digests.MpEmailPref>();

    public DbSet<Digests.MpMailToken> MpMailTokens => Set<Digests.MpMailToken>();

    public DbSet<Digests.IntroductionLetter> IntroductionLetters => Set<Digests.IntroductionLetter>();

    public DbSet<Digests.Suppression> Suppressions => Set<Digests.Suppression>();

    public DbSet<Lt2.Notifications.Follows.Follow> Follows => Set<Lt2.Notifications.Follows.Follow>();

    public DbSet<Lt2.Notifications.Follows.NoticeSetting> NoticeSettings => Set<Lt2.Notifications.Follows.NoticeSetting>();

    public DbSet<Lt2.Notifications.Follows.BillEvent> BillEvents => Set<Lt2.Notifications.Follows.BillEvent>();

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
        modelBuilder.Entity<Digests.DigestRun>(b =>
        {
            b.ToTable("digest_run");
            b.HasKey(x => x.Id);
            b.Property(x => x.Kind).HasMaxLength(20);
            b.Property(x => x.Status).HasMaxLength(20);
            b.Property(x => x.Statement).HasMaxLength(StaffStatements.MaxLength);
            b.HasIndex(x => x.RunDate);
            b.HasIndex(x => x.Status).HasFilter("status = 'waiting'");
        });
        modelBuilder.Entity<Digests.DigestItem>(b =>
        {
            b.ToTable("digest_item");
            b.HasKey(x => x.Id);
            b.Property(x => x.BillNumber).HasMaxLength(40);
            b.Property(x => x.Stage).HasMaxLength(16);
            b.Property(x => x.Reasons).HasColumnType("jsonb");
            b.HasIndex(x => x.RunId);
            b.HasIndex(x => x.RecordId);
            b.HasOne<Digests.DigestRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Digests.DigestDelivery>(b =>
        {
            b.ToTable("digest_delivery");
            b.HasKey(x => new { x.RunId, x.MpSlug, x.Recipient });
            b.Property(x => x.MpSlug).HasMaxLength(200);
            b.Property(x => x.Recipient).HasMaxLength(10).HasDefaultValue(Digests.Recipients.Official);
            b.Property(x => x.Status).HasMaxLength(20);
            b.Property(x => x.PartyNumbers).HasColumnType("jsonb");
            b.HasIndex(x => new { x.SeimasPersonId, x.Status });
            b.HasOne<Digests.DigestRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
        });

        // Design 8.19.4 to 8.19.6: the MPs' choices, their links, the introduction letter and the suppression list.
        modelBuilder.Entity<Digests.MpEmailPref>(b =>
        {
            b.ToTable("mp_email_pref");
            b.HasKey(x => x.SeimasPersonId);
            b.Property(x => x.SeimasPersonId).ValueGeneratedNever();
            b.Property(x => x.Frequency).HasMaxLength(16);
            b.Property(x => x.ExtraAddress).HasMaxLength(254);
        });
        modelBuilder.Entity<Digests.MpMailToken>(b =>
        {
            b.ToTable("mp_mail_token");
            b.HasKey(x => x.Id);
            b.Property(x => x.Kind).HasMaxLength(16);
            b.Property(x => x.TokenHash).HasMaxLength(64);
            b.HasIndex(x => x.TokenHash).IsUnique();
            b.HasIndex(x => new { x.SeimasPersonId, x.Kind });
        });
        modelBuilder.Entity<Digests.IntroductionLetter>(b =>
        {
            b.ToTable("introduction_letter");
            b.HasKey(x => x.Version);
            b.Property(x => x.Version).ValueGeneratedNever();
            b.Property(x => x.Subject).HasMaxLength(200);
            b.Property(x => x.Text).HasMaxLength(10000);
            b.Property(x => x.SignerName).HasMaxLength(200);
            b.Property(x => x.SignerRole).HasMaxLength(200);
        });
        modelBuilder.Entity<Digests.Suppression>(b =>
        {
            b.ToTable("suppression");
            b.HasKey(x => x.Id);
            b.Property(x => x.AddressHash).HasMaxLength(64);
            b.Property(x => x.Reason).HasMaxLength(16);
            b.Property(x => x.Statement).HasMaxLength(StaffStatements.MaxLength);
            b.HasIndex(x => x.AddressHash).IsUnique();
        });

        // Design 5.4, 8.11.2: follows (private), the follow-email switches, and the events followers are told about.
        modelBuilder.Entity<Lt2.Notifications.Follows.Follow>(b =>
        {
            b.ToTable("follow");
            b.HasKey(x => new { x.UserId, x.Kind, x.Key });
            b.Property(x => x.Kind).HasMaxLength(16);
            b.Property(x => x.Key).HasMaxLength(40);
            b.HasIndex(x => new { x.Kind, x.Key });
        });
        modelBuilder.Entity<Lt2.Notifications.Follows.NoticeSetting>(b =>
        {
            b.ToTable("notice_setting");
            b.HasKey(x => x.UserId);
        });
        modelBuilder.Entity<Lt2.Notifications.Follows.BillEvent>(b =>
        {
            b.ToTable("bill_event");
            b.HasKey(x => new { x.BillNumber, x.Kind, x.Key });
            b.Property(x => x.BillNumber).HasMaxLength(40);
            b.Property(x => x.Kind).HasMaxLength(10);
            b.Property(x => x.Key).HasMaxLength(40);
            b.Property(x => x.Outcome).HasMaxLength(20);
            b.HasIndex(x => x.Outcome).HasFilter("outcome = 'waiting'");
        });
    }
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
