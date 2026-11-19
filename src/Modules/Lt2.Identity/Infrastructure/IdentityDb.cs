using Lt2.Identity.Domain;
using Lt2.SharedKernel;
using Lt2.SharedKernel.Persistence;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Lt2.Identity.Infrastructure;

/// <summary>
/// ASP.NET Core Identity tables (schema version 3, with passkeys) and the module's own tables, all in schema
/// <c>identity</c>. Also stores the data-protection key ring.
/// </summary>
internal sealed class IdentityDb(DbContextOptions<IdentityDb> options)
    : IdentityDbContext<User, Role, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<Profile> Profiles => Set<Profile>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<InvitationDaily> InvitationDailies => Set<InvitationDaily>();

    public DbSet<FeedbackItem> Feedback => Set<FeedbackItem>();

    public DbSet<Consent> Consents => Set<Consent>();

    public DbSet<SignInCode> SignInCodes => Set<SignInCode>();

    public DbSet<BotCheckUse> BotCheckUses => Set<BotCheckUse>();

    public DbSet<KnownDevice> KnownDevices => Set<KnownDevice>();

    public DbSet<EmailChange> EmailChanges => Set<EmailChange>();

    public DbSet<EmailRestore> EmailRestores => Set<EmailRestore>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<RoleActivation> RoleActivations => Set<RoleActivation>();

    public DbSet<PartySupport> PartySupports => Set<PartySupport>();

    public DbSet<PoliticianLink> PoliticianLinks => Set<PoliticianLink>();

    public DbSet<PoliticianToken> PoliticianTokens => Set<PoliticianToken>();

    public DbSet<RetiredProfileId> RetiredProfileIds => Set<RetiredProfileId>();

    public DbSet<ProfileReport> ProfileReports => Set<ProfileReport>();

    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(IdentityModule.Schema);

        builder.Entity<User>().ToTable("user");
        builder.Entity<Role>().ToTable("role");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_role");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claim");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_login");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_token");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claim");
        builder.Entity<IdentityUserPasskey<Guid>>().ToTable("user_passkey");
        builder.Entity<DataProtectionKey>().ToTable("data_protection_key");

        builder.Entity<Role>().HasData(SeedRoles.All);

        builder.Entity<Profile>(b =>
        {
            b.ToTable("profile");
            b.HasKey(x => x.UserId);
            b.HasOne<User>().WithOne().HasForeignKey<Profile>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.Property(x => x.DisplayName).HasMaxLength(100);
            b.Property(x => x.NormalizedEmail).HasMaxLength(256);
            b.HasIndex(x => x.NormalizedEmail).IsUnique();
            b.Property(x => x.VerificationLevel).HasMaxLength(20);
            b.HasOne<Invitation>().WithMany().HasForeignKey(x => x.InvitationId).OnDelete(DeleteBehavior.SetNull);
            b.Property(x => x.InvitationCode).HasMaxLength(64);
            b.Property(x => x.StaffPasskeyId).HasMaxLength(1024);
            b.Ignore(x => x.IsPending);
            b.HasIndex(x => x.CreatedAt).HasFilter("registered_at IS NULL").HasDatabaseName("ix_profile_pending_created_at");

            // Design 8.25: "Apie mane" is any length (Texts:MaxLength is checked where it is written); one id per public profile.
            b.Property(x => x.About).HasColumnType("text");
            b.Property(x => x.AboutHiddenStatement).HasMaxLength(2000);
        b.Property(x => x.LockedStatement).HasMaxLength(2000);
            b.Property(x => x.PublicProfileId).HasMaxLength(10);
            b.HasIndex(x => x.PublicProfileId).IsUnique().HasFilter("public_profile_id IS NOT NULL");
        });

        builder.Entity<RetiredProfileId>(b =>
        {
            b.ToTable("retired_profile_id");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasMaxLength(10);
        });

        builder.Entity<ProfileReport>(b =>
        {
            b.ToTable("profile_report");
            b.HasKey(x => x.Id);
            b.Property(x => x.Ground).HasMaxLength(32);
            b.Property(x => x.Text).HasMaxLength(1000);
            b.Property(x => x.Status).HasMaxLength(8);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.TargetUserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.ReporterUserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TargetUserId, x.Status });
            b.HasIndex(x => new { x.ReporterUserId, x.TargetUserId }).IsUnique().HasFilter("status = 'open'");
        });

        builder.Entity<Invitation>(b =>
        {
            b.ToTable("invitation");
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(64);
            b.HasIndex(x => x.Code).IsUnique();
            b.Property(x => x.Lookup).HasMaxLength(64)
                .HasComputedColumnSql("upper(replace(replace(code, '-', ''), ' ', ''))", stored: true);
            b.HasIndex(x => x.Lookup).IsUnique();
            b.Property(x => x.Kind).HasMaxLength(16).HasDefaultValue(InvitationKinds.Team);
            b.Property(x => x.Level).HasDefaultValue(1);
            b.HasIndex(x => x.IssuedByUserId);

            // One current code per organisation (design 8.14.14, stage B); replaced ones keep counting what they brought.
            b.Property(x => x.OrganisationOff).HasMaxLength(16);
            b.HasIndex(x => x.OrganisationId).IsUnique()
                .HasFilter("kind = 'organisation' AND replaced_at IS NULL")
                .HasDatabaseName("ix_invitation_organisation_current");

            // One current code per member (design 6.8), as for organisations.
            b.HasIndex(x => x.IssuedByUserId, "ix_invitation_personal_current").IsUnique()
                .HasFilter("kind = 'personal' AND replaced_at IS NULL")
                .HasDatabaseName("ix_invitation_personal_current");
            b.ToTable(t => t.HasCheckConstraint("ck_invitation_uses_left", "uses_left >= 0"));
        });

        builder.Entity<FeedbackItem>(b =>
        {
            b.ToTable("feedback");
            b.HasKey(x => x.Id);
            b.Property(x => x.Kind).HasMaxLength(16);
            b.Property(x => x.Text).HasMaxLength(2000);
            b.Property(x => x.PagePath).HasMaxLength(200);
            b.Property(x => x.Browser).HasMaxLength(60);
            b.Property(x => x.Status).HasMaxLength(8);
            b.Property(x => x.Note).HasMaxLength(1000);
            b.Property(x => x.Replies).HasColumnType("jsonb");
            b.HasIndex(x => new { x.Status, x.CreatedOn });

            // A linked item goes with its account (design 8.10).
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<InvitationDaily>(b =>
        {
            b.ToTable("invitation_daily");
            b.HasKey(x => new { x.InvitationId, x.Day });
            b.HasOne<Invitation>().WithMany().HasForeignKey(x => x.InvitationId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Consent>(b =>
        {
            b.ToTable("consent");
            b.HasKey(x => x.Id);
            b.Property(x => x.Kind).HasMaxLength(50);
            b.Property(x => x.TextVersion).HasMaxLength(50);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.UserId, x.Kind });
        });

        builder.Entity<SignInCode>(b =>
        {
            b.ToTable("sign_in_code");
            b.HasKey(x => x.Id);
            b.Property(x => x.CodeHash).HasMaxLength(32);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.UserId, x.CreatedAt });

            // A forgotten browser's codes go with it: never left as codes any browser could use (design 8.20.2).
            b.HasOne<KnownDevice>().WithMany().HasForeignKey(x => x.KnownDeviceId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<KnownDevice>(b =>
        {
            b.ToTable("known_device");
            b.HasKey(x => x.Id);
            b.Property(x => x.IdentifierHash).HasMaxLength(32);
            b.Property(x => x.Label).HasMaxLength(100);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.UserId, x.IdentifierHash }).IsUnique();
            b.HasIndex(x => x.LastSeenAt);
        });

        // Design 6.2, changing the email address: the codes to a new address, counted per address; the old address's link back.
        builder.Entity<EmailChange>(b =>
        {
            b.ToTable("email_change");
            b.HasKey(x => x.Id);
            b.Property(x => x.Email).HasMaxLength(256);
            b.Property(x => x.NormalizedEmail).HasMaxLength(256);
            b.Property(x => x.CodeHash).HasMaxLength(32);
            b.Property(x => x.Proof).HasMaxLength(10);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.UserId, x.CreatedAt });
            b.HasIndex(x => new { x.NormalizedEmail, x.CreatedAt });
        });

        builder.Entity<BotCheckUse>(b =>
        {
            b.ToTable("bot_check_use");
            b.HasKey(x => x.Signature);
            b.Property(x => x.Signature).HasMaxLength(64);
            b.HasIndex(x => x.ExpiresAt);
        });

        builder.Entity<EmailRestore>(b =>
        {
            b.ToTable("email_restore");
            b.HasKey(x => x.Id);
            b.Property(x => x.Email).HasMaxLength(256);
            b.Property(x => x.NormalizedEmail).HasMaxLength(256);
            b.Property(x => x.TokenHash).HasMaxLength(32);
            b.HasIndex(x => x.TokenHash).IsUnique();
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.UserId);
        });

        builder.Entity<Session>(b =>
        {
            b.ToTable("session");
            b.HasKey(x => x.Id);
            b.Property(x => x.Method).HasMaxLength(10);
            b.Property(x => x.Device).HasMaxLength(100);
            b.Property(x => x.PasskeyId).HasMaxLength(1024);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.UserId);
            b.HasIndex(x => x.ExpiresAt);
        });

        builder.Entity<PartySupport>(b =>
        {
            b.ToTable("party_support");
            b.HasKey(x => x.UserId);
            b.Property(x => x.Party).HasMaxLength(200);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.Party);
        });

        builder.Entity<RoleActivation>(b =>
        {
            b.ToTable("role_activation");
            b.HasKey(x => x.Id);
            b.Property(x => x.Role).HasMaxLength(20);
            b.Property(x => x.CodeHash).HasMaxLength(32);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.UserId, x.Role });
        });

        // Design 8.5.2, 8.5.3: one open link per account, one open MP link per person; both go with the account.
        builder.Entity<PoliticianLink>(b =>
        {
            b.ToTable("politician_link");
            b.HasKey(x => x.Id);
            b.Property(x => x.Part).HasMaxLength(10);
            b.Property(x => x.VerifiedBy).HasMaxLength(20);
            b.Property(x => x.EndReason).HasMaxLength(20);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.UserId).IsUnique().HasFilter("ended_at IS NULL").HasDatabaseName("ix_politician_link_account_open");
            b.HasIndex(x => x.SeimasPersonId).IsUnique().HasFilter("ended_at IS NULL AND part = 'mp'").HasDatabaseName("ix_politician_link_mp_open");
            b.HasIndex(x => new { x.SeimasPersonId, x.Part });
        });

        builder.Entity<PoliticianToken>(b =>
        {
            b.ToTable("politician_token");
            b.HasKey(x => x.Id);
            b.Property(x => x.Kind).HasMaxLength(10);
            b.Property(x => x.TokenHash).HasMaxLength(32);
            b.HasIndex(x => x.TokenHash).IsUnique();
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.SeimasPersonId, x.Kind, x.CreatedAt });
        });
    }

    /// <summary>
    /// Options for use outside the web host (migrations, tools). Identity reads its schema version from the application
    /// services, so a minimal provider with version 3 (passkeys) is attached.
    /// </summary>
    internal static DbContextOptions<IdentityDb> StandaloneOptions(string connectionString)
    {
        var services = new ServiceCollection();
        services.Configure<IdentityOptions>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3);
        var builder = new DbContextOptionsBuilder<IdentityDb>();
        builder.UseLt2Npgsql(connectionString, IdentityModule.Schema)
            .UseApplicationServiceProvider(services.BuildServiceProvider());
        return builder.Options;
    }

    internal static void Configure(DbContextOptionsBuilder options, NpgsqlDataSource dataSource) =>
        options.UseLt2Npgsql(dataSource, IdentityModule.Schema);
}

/// <summary>The three roles, seeded by the migration with fixed identifiers.</summary>
internal static class SeedRoles
{
    public static readonly Guid MemberId = Guid.Parse("0193a000-0000-7000-8000-000000000001");

    public static Guid IdOf(string role) => All.Single(r => r.Name == role).Id;

    public static readonly Role[] All =
    [
        new()
        {
            Id = Guid.Parse("0193a000-0000-7000-8000-000000000001"),
            Name = Roles.Member,
            NormalizedName = Roles.Member.ToUpperInvariant(),
            ConcurrencyStamp = "0193a000-0000-7000-8000-000000000001",
        },
        new()
        {
            Id = Guid.Parse("0193a000-0000-7000-8000-000000000002"),
            Name = Roles.Moderator,
            NormalizedName = Roles.Moderator.ToUpperInvariant(),
            ConcurrencyStamp = "0193a000-0000-7000-8000-000000000002",
        },
        new()
        {
            Id = Guid.Parse("0193a000-0000-7000-8000-000000000003"),
            Name = Roles.Administrator,
            NormalizedName = Roles.Administrator.ToUpperInvariant(),
            ConcurrencyStamp = "0193a000-0000-7000-8000-000000000003",
        },
    ];
}

/// <summary>Used by <c>dotnet ef</c> only.</summary>
internal sealed class IdentityDbDesignTimeFactory : IDesignTimeDbContextFactory<IdentityDb>
{
    public IdentityDb CreateDbContext(string[] args) => new(IdentityDb.StandaloneOptions("Host=localhost;Database=lt2_design"));
}

internal sealed class IdentityModuleDatabase : IModuleDatabase
{
    public string Module => "Identity";

    public IReadOnlyList<string> Schemas => [IdentityModule.Schema];

    public async Task MigrateAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var db = new IdentityDb(IdentityDb.StandaloneOptions(connectionString));
        await db.Database.MigrateAsync(cancellationToken);
    }
}
