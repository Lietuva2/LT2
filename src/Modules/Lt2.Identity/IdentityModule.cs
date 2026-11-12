using System.Security.Claims;
using Lt2.Identity.Application;
using Lt2.Identity.Application.Account;
using Lt2.Identity.Application.Passkeys;
using Lt2.Identity.Application.Register;
using Lt2.Identity.Application.SignIn;
using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Lt2.Identity;

/// <summary>Registration of the Identity module: accounts, sign-in by code and passkey, sessions, data protection.</summary>
public static class IdentityModule
{
    public const string Schema = "identity";

    /// <summary>Session cookie (design 6.2): <c>__Host-</c> prefixed, Secure, HttpOnly, SameSite=Lax.</summary>
    public const string SessionCookieName = "__Host-lt2";

    /// <summary>Short-lived cookie holding the passkey challenge state.</summary>
    public const string PasskeyCookieName = "__Host-lt2.passkey";

    /// <summary>Where the server-rendered pages send people who need to sign in.</summary>
    public const string SignInPath = "/prisijungti";

    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<IdentityDb>((sp, options) =>
            IdentityDb.Configure(options, sp.GetRequiredService<NpgsqlDataSource>()));

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddIdentityCookies(cookies =>
            {
                cookies.ApplicationCookie!.Configure(o =>
                {
                    ConfigureCookie(o.Cookie, SessionCookieName);
                    o.ExpireTimeSpan = TimeSpan.FromDays(30);
                    o.SlidingExpiration = true;
                    o.LoginPath = SignInPath;
                    o.AccessDeniedPath = "/";
                    o.Events.OnRedirectToLogin = context => ApiAwareRedirect(context, StatusCodes.Status401Unauthorized);
                    o.Events.OnRedirectToAccessDenied = context => ApiAwareRedirect(context, StatusCodes.Status403Forbidden);
                });
                cookies.TwoFactorUserIdCookie!.Configure(o =>
                {
                    ConfigureCookie(o.Cookie, PasskeyCookieName);
                    o.ExpireTimeSpan = TimeSpan.FromMinutes(5);
                });
                cookies.ExternalCookie!.Configure(o => ConfigureCookie(o.Cookie, "__Host-lt2.external"));
                cookies.TwoFactorRememberMeCookie!.Configure(o => ConfigureCookie(o.Cookie, "__Host-lt2.remember"));
            });

        services.AddSingleton<SessionTicketStore>();
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<SessionTicketStore>((options, store) => options.SessionStore = store);

        services.AddIdentityCore<User>(options =>
            {
                options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
                options.User.RequireUniqueEmail = false;
                options.SignIn.RequireConfirmedAccount = false;
                options.Lockout.AllowedForNewUsers = false;
            })
            .AddRoles<Role>()
            .AddEntityFrameworkStores<IdentityDb>()
            .AddSignInManager();
        services.Configure<IdentityPasskeyOptions>(configuration.GetSection("Passkeys"));

        services.AddDataProtection()
            .SetApplicationName("lt2")
            .PersistKeysToDbContext<IdentityDb>();
        services.AddOptions<KeyEncryptionOptions>().Bind(configuration.GetSection(KeyEncryptionOptions.Section));
        if (!string.IsNullOrEmpty(configuration[$"{KeyEncryptionOptions.Section}:KeyEncryptionKey"]))
        {
            services.AddOptions<KeyManagementOptions>()
                .Configure<IOptions<KeyEncryptionOptions>>((o, key) => o.XmlEncryptor = new AesGcmXmlEncryptor(key));
        }

        services.AddScoped<SignInCodeIssuer>();
        services.AddScoped<RegisterHandler>();
        services.AddScoped<RequestCodeHandler>();
        services.AddScoped<CodeSignInHandler>();
        services.AddScoped<PasskeyHandlers>();
        services.AddScoped<AccountHandlers>();
        services.AddScoped<IIdentityModule, IdentityModuleFacade>();
        services.AddScoped<IIdentityMaintenance, MaintenanceHandler>();
        services.AddSingleton<IModuleDatabase, IdentityModuleDatabase>();
        return services;
    }

    /// <summary>Registers only what the worker needs: the database and housekeeping.</summary>
    public static IServiceCollection AddIdentityMaintenance(this IServiceCollection services)
    {
        services.AddDbContext<IdentityDb>((sp, options) =>
            IdentityDb.Configure(options, sp.GetRequiredService<NpgsqlDataSource>()));
        services.AddScoped<IIdentityMaintenance, MaintenanceHandler>();
        return services;
    }

    private static void ConfigureCookie(CookieBuilder cookie, string name)
    {
        cookie.Name = name;
        cookie.HttpOnly = true;
        cookie.SecurePolicy = CookieSecurePolicy.Always;
        cookie.SameSite = SameSiteMode.Lax;
        cookie.Path = "/";
        cookie.Domain = null;
    }

    private static Task ApiAwareRedirect(Microsoft.AspNetCore.Authentication.RedirectContext<CookieAuthenticationOptions> context, int apiStatus)
    {
        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = apiStatus;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }
}

internal sealed class IdentityModuleFacade(
    RegisterHandler register,
    RequestCodeHandler requestCode,
    CodeSignInHandler codeSignIn,
    PasskeyHandlers passkeys,
    AccountHandlers account,
    SignInManager<User> signIn) : IIdentityModule
{
    public Task<Result<Unit>> RegisterAsync(Registration registration, CancellationToken cancellationToken) =>
        register.HandleAsync(registration, cancellationToken);

    public Task<Result<Unit>> RequestSignInCodeAsync(string email, CancellationToken cancellationToken) =>
        requestCode.HandleAsync(email, cancellationToken);

    public Task<Result<Unit>> SignInWithCodeAsync(string email, string code, CancellationToken cancellationToken) =>
        codeSignIn.HandleAsync(email, code, cancellationToken);

    public Task SignOutAsync() => signIn.SignOutAsync();

    public Task<Result<string>> GetPasskeyCreationOptionsAsync(ClaimsPrincipal user, CancellationToken cancellationToken) =>
        passkeys.CreationOptionsAsync(user, cancellationToken);

    public Task<Result<Unit>> AddPasskeyAsync(ClaimsPrincipal user, string credentialJson, string? name, CancellationToken cancellationToken) =>
        passkeys.AddAsync(user, credentialJson, name, cancellationToken);

    public Task<Result<string>> GetPasskeyRequestOptionsAsync(CancellationToken cancellationToken) =>
        passkeys.RequestOptionsAsync();

    public Task<Result<Unit>> SignInWithPasskeyAsync(string credentialJson, CancellationToken cancellationToken) =>
        passkeys.SignInAsync(credentialJson);

    public Task<Result<AccountSummary>> GetAccountAsync(ClaimsPrincipal user, CancellationToken cancellationToken) =>
        account.GetAsync(user, cancellationToken);

    public Task<IReadOnlyList<SessionSummary>> GetSessionsAsync(ClaimsPrincipal user, CancellationToken cancellationToken) =>
        account.SessionsAsync(user, cancellationToken);

    public Task<Result<Unit>> EndSessionAsync(ClaimsPrincipal user, Guid sessionId, CancellationToken cancellationToken) =>
        account.EndSessionAsync(user, sessionId, cancellationToken);
}

/// <summary>Operational commands run by the migrator (seeding a trial invitation, granting a role).</summary>
public static class IdentityAdministration
{
    /// <summary>Creates the invitation if no invitation with that code exists. Returns whether one was created.</summary>
    public static async Task<bool> EnsureInvitationAsync(string connectionString, string code, int uses, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        await using var db = new IdentityDb(IdentityDb.StandaloneOptions(connectionString));
        var normalized = Invitation.NormalizeCode(code);
        if (await db.Invitations.AnyAsync(i => i.Code == normalized, cancellationToken))
        {
            return false;
        }

        var clock = TimeProvider.System;
        db.Invitations.Add(new Invitation
        {
            Id = Ids.New(clock),
            Code = normalized,
            UsesLeft = uses,
            CreatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Gives the account with this email address a role. Returns false if there is no such account.</summary>
    public static async Task<bool> GrantRoleAsync(string connectionString, string email, string role, CancellationToken cancellationToken)
    {
        if (!Roles.All.Contains(role))
        {
            throw new ArgumentException($"Unknown role '{role}'.", nameof(role));
        }

        var normalized = EmailAddress.Normalize(email) ?? throw new ArgumentException("Not an email address.", nameof(email));
        await using var db = new IdentityDb(IdentityDb.StandaloneOptions(connectionString));
        var userId = await db.Profiles.Where(p => p.NormalizedEmail == normalized).Select(p => (Guid?)p.UserId)
            .FirstOrDefaultAsync(cancellationToken);
        if (userId is null)
        {
            return false;
        }

        var roleId = SeedRoles.All.Single(r => r.Name == role).Id;
        if (!await db.UserRoles.AnyAsync(r => r.UserId == userId && r.RoleId == roleId, cancellationToken))
        {
            db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = userId.Value, RoleId = roleId });
            await db.SaveChangesAsync(cancellationToken);
        }

        return true;
    }
}
