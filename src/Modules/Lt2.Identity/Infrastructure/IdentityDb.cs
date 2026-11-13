using Lt2.Identity.Domain;
using Lt2.SharedKernel;
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

    public DbSet<Consent> Consents => Set<Consent>();

    public DbSet<SignInCode> SignInCodes => Set<SignInCode>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<RoleActivation> RoleActivations => Set<RoleActivation>();

    public DbSet<PartySupport> PartySupports => Set<PartySupport>();

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
            b.ToTable(t => t.HasCheckConstraint("ck_invitation_uses_left", "uses_left >= 0"));
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
        builder.UseNpgsql(connectionString, o => o.MigrationsHistoryTable("__ef_migrations_history", IdentityModule.Schema))
            .UseSnakeCaseNamingConvention()
            .UseApplicationServiceProvider(services.BuildServiceProvider());
        return builder.Options;
    }

    internal static void Configure(DbContextOptionsBuilder options, NpgsqlDataSource dataSource) =>
        options.UseNpgsql(dataSource, o => o.MigrationsHistoryTable("__ef_migrations_history", IdentityModule.Schema))
            .UseSnakeCaseNamingConvention();
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
