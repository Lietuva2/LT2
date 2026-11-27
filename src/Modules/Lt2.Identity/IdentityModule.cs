using System.Security.Claims;
using Lt2.Identity.Application;
using Lt2.Identity.Application.Account;
using Lt2.Identity.Application.Consents;
using Lt2.Identity.Application.Passkeys;
using Lt2.Identity.Application.Register;
using Lt2.Identity.Application.SignIn;
using Lt2.Identity.Application.Staff;
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

    /// <summary>
    /// A known browser's cookie (design 6.2, 8.20.2): a random identifier for a year, renewed on every sign-in and kept on signing
    /// out. It gives no access by itself; it only chooses which sign-in code allowance and which codes apply.
    /// </summary>
    public const string DeviceCookieName = "__Host-lt2.device";

    /// <summary>Where the server-rendered pages send people who need to sign in.</summary>
    public const string SignInPath = "/prisijungti";

    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddIdentityDb();

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

        // The security stamp is checked every 30 minutes and the principal rebuilt from the account; the session's own claims
        // (how it was signed in, the passkey, its key) are carried over, or a passkey session would lose them (SessionClaims).
        services.Configure<SecurityStampValidatorOptions>(o => o.OnRefreshingPrincipal = SessionClaims.KeepAsync);

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

        services.AddHttpContextAccessor();
        services.AddSingleton<DeviceKeys>();
        services.AddSingleton<BotCheckKey>();
        services.AddScoped<BotCheck>();
        services.AddScoped<KnownBrowsers>();
        services.AddScoped<SignInCodeIssuer>();
        services.AddSingleton(RegistrationWithoutEmail.From(configuration));
        services.AddScoped<RegisterHandler>();
        services.AddScoped<CompleteRegistrationHandler>();
        services.AddScoped<RequestCodeHandler>();
        services.AddScoped<CodeSignInHandler>();
        services.AddScoped<PasskeyHandlers>();
        services.AddScoped<AccountHandlers>();

        // Design 6.2: needs IWritingRestrictions (Appeals) from the host.
        services.AddScoped<StaffAccess>();
        services.AddScoped<AccountEvents>();
        services.AddScoped<StaffHandlers>();
        services.AddScoped<IIdentityAdministration, RoleAdministration>();
        services.AddScoped<PositionConsentHandlers>();
        services.AddScoped<TermsHandlers>();
        services.AddScoped<PartySupportHandlers>();
        services.AddScoped<AccountDataHandlers>();
        services.AddScoped<Application.Email.EmailChangeHandlers>();

        // Design 8.5: MPs on the platform (the web host composes Parliament's IMpDirectory and Appeals' IPositionErasure).
        services.AddScoped<Application.Politicians.PoliticianAccess>();
        services.AddScoped<Application.Politicians.PoliticianHandlers>();
        services.AddScoped<IPoliticians>(sp => sp.GetRequiredService<Application.Politicians.PoliticianHandlers>());
        Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddScoped<IMpLinks, Application.Politicians.MpLinks>(services);
        services.AddScoped<IInvitations, Application.Invitations.InvitationHandlers>();
        services.AddMemoryCache();
        services.AddScoped<IInventedMembersNotice, InventedMembersNotice>();
        services.AddScoped<IMemberCounts, MemberCountReads>();
        services.AddScoped<IOrganisationInvitations, Application.Invitations.OrganisationInvitationHandlers>();
        services.AddScoped<IFeedback, Application.Feedback.FeedbackHandlers>();
        services.AddSingleton<IParticipants, ParticipantDirectory>();
        services.AddSingleton<IPartySupporters, PartySupporterDirectory>();
        Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton<IStaffAddresses, StaffAddressDirectory>(services);
        services.AddSingleton<IOrganisationCodeSwitch, Application.Invitations.OrganisationCodeSwitch>();

        // Design 8.25: public profiles (the host composes Notifications' IMemberFollowEnd and Appeals' IWritingRestrictions).
        services.AddScoped<Application.Profiles.ProfileHandlers>();
        services.AddScoped<Application.Profiles.ProfileModerationHandlers>();
        services.AddScoped<IMemberProfiles, Application.Profiles.MemberProfiles>();
        Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton<IPublicProfiles, Application.Profiles.PublicProfileDirectory>(services);
        services.AddScoped<IIdentityModule, IdentityModuleFacade>();
        services.AddScoped<IIdentityMaintenance, MaintenanceHandler>();
        services.AddSingleton<IModuleDatabase, IdentityModuleDatabase>();
        return services;
    }

    /// <summary>
    /// Registers only role administration and its database, for the migrator (the first administrator, design 6.2).
    /// The host provides <see cref="IOutbox"/> and <see cref="IAuditLog"/>.
    /// </summary>
    public static IServiceCollection AddIdentityAdministration(this IServiceCollection services)
    {
        services.AddIdentityDb();
        services.AddScoped<AccountEvents>();
        services.AddScoped<IIdentityAdministration, RoleAdministration>();
        return services;
    }

    /// <summary>Registers <see cref="IParticipants"/> alone, for hosts that run Appeals work without the web host's Identity.</summary>
    public static IServiceCollection AddIdentityDirectory(this IServiceCollection services)
    {
        services.AddSingleton<IParticipants, ParticipantDirectory>();
        services.AddSingleton<IPartySupporters, PartySupporterDirectory>();

        // Design 8.19.2: the moderators told when a summary is prepared, for the worker's summaries.
        Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton<IStaffAddresses, StaffAddressDirectory>(services);

        // Design 8.25: who has a public profile, for the follow emails and the names beside arguments in any host.
        Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton<IPublicProfiles, Application.Profiles.PublicProfileDirectory>(services);

        // An organisation's codes follow it when Appeals work in another host closes one (design 8.14.14, stage B).
        services.AddSingleton<IOrganisationCodeSwitch, Application.Invitations.OrganisationCodeSwitch>();

        // Design 8.5.5: where a moderator's notice about an MP's response goes, for the Appeals module in any host.
        Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddScoped<IMpLinks, Application.Politicians.MpLinks>(services);
        return services;
    }

    /// <summary>Registers only what the worker needs: the database and housekeeping.</summary>
    public static IServiceCollection AddIdentityMaintenance(this IServiceCollection services)
    {
        services.AddIdentityDb();
        services.AddScoped<IIdentityMaintenance, MaintenanceHandler>();

        // The worker's daily notice of new feedback (design 8.10).
        services.AddScoped<IFeedback, Application.Feedback.FeedbackHandlers>();
        return services;
    }

    /// <summary>
    /// The module's database. Identity reads its schema version (3, with passkeys) from the application services when it
    /// builds the model, so every host that uses the database must set it, not only the web host. A factory as well as the
    /// scoped context: the components of a page render at the same time in one scope, so the module's services take a context
    /// of their own per operation. The scoped context is for ASP.NET Core Identity's stores and the flows that write through
    /// them in one transaction (registration, sign-in, passkeys, staff sessions, an MP's link changes, deleting the account).
    /// </summary>
    private static void AddIdentityDb(this IServiceCollection services)
    {
        services.Configure<IdentityOptions>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3);
        services.AddDbContextFactory<IdentityDb>(
            (sp, options) => IdentityDb.Configure(options, sp.GetRequiredService<NpgsqlDataSource>()), ServiceLifetime.Scoped);
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

        // A page that needs a signed-in member (the account screens, design 8.21) sends a visitor to sign in with the page to
        // come back to after the '#' (design 6.8, 8.11.5: "tikslas"), which the sign-in page accepts only for its own list of
        // pages; anything else signs in to the account. The page's address is never put in a query.
        var request = context.Request;
        context.Response.Redirect(apiStatus == StatusCodes.Status401Unauthorized
            ? $"{request.Scheme}://{request.Host}{request.PathBase}{SignInPath}#tikslas={Uri.EscapeDataString(request.PathBase + request.Path)}"
            : context.RedirectUri);
        return Task.CompletedTask;
    }
}

internal sealed class IdentityModuleFacade(
    RegisterHandler register,
    CompleteRegistrationHandler completeRegistration,
    RequestCodeHandler requestCode,
    CodeSignInHandler codeSignIn,
    PasskeyHandlers passkeys,
    AccountHandlers account,
    StaffHandlers staff,
    PositionConsentHandlers consents,
    TermsHandlers terms,
    PartySupportHandlers parties,
    AccountDataHandlers data,
    KnownBrowsers browsers,
    Application.Email.EmailChangeHandlers emailChange,
    BotCheck proofOfWork,
    SignInManager<User> signIn) : IIdentityModule
{
    public Task<Result<Unit>> SendEmailProofCodeAsync(ClaimsPrincipal user, CancellationToken cancellationToken) =>
        emailChange.SendProofCodeAsync(user, cancellationToken);

    public Task<Result<Unit>> ChangeEmailAsync(ClaimsPrincipal user, string email, string? proofCode, CancellationToken cancellationToken) =>
        emailChange.ChangeAsync(user, email, proofCode, cancellationToken);

    public Task<Result<string>> ConfirmEmailChangeAsync(ClaimsPrincipal user, string code, string? ipHash, CancellationToken cancellationToken) =>
        emailChange.ConfirmAsync(user, code, ipHash, cancellationToken);

    public Task<Result<Unit>> RestoreEmailAsync(string token, string? ipHash, CancellationToken cancellationToken) =>
        emailChange.RestoreAsync(token, ipHash, cancellationToken);

    public Task<Result<RegistrationOutcome>> RegisterAsync(Registration registration, CancellationToken cancellationToken) =>
        register.HandleAsync(registration, cancellationToken);

    public Task<Result<Unit>> CompleteRegistrationAsync(ClaimsPrincipal user, RegistrationCompletion completion, CancellationToken cancellationToken) =>
        completeRegistration.HandleAsync(user, completion, cancellationToken);

    public Task<Result<Unit>> RequestSignInCodeAsync(string email, string? botCheck, CancellationToken cancellationToken) =>
        requestCode.HandleAsync(email, botCheck, cancellationToken);

    public Task<BotChallenge?> NewBotChallengeAsync(CancellationToken cancellationToken) => proofOfWork.NewChallengeAsync(cancellationToken);

    public Task<Result<SignInOutcome>> SignInWithCodeAsync(string email, string code, CancellationToken cancellationToken) =>
        codeSignIn.HandleAsync(email, code, cancellationToken);

    public Task SignOutAsync() => signIn.SignOutAsync();

    public Task<Result<string>> GetPasskeyCreationOptionsAsync(ClaimsPrincipal user, CancellationToken cancellationToken) =>
        passkeys.CreationOptionsAsync(user, cancellationToken);

    public Task<Result<Unit>> AddPasskeyAsync(ClaimsPrincipal user, string credentialJson, string? name, string? ipHash, CancellationToken cancellationToken) =>
        passkeys.AddAsync(user, credentialJson, name, ipHash, cancellationToken);

    public Task<Result<Unit>> ConfirmPasskeyAsync(ClaimsPrincipal user, string credentialJson, CancellationToken cancellationToken) =>
        staff.ConfirmPasskeyAsync(user, credentialJson, cancellationToken);

    public Task<Result<Unit>> StartStaffSessionAsync(ClaimsPrincipal user, string credentialJson, string? ipHash, CancellationToken cancellationToken) =>
        staff.StartStaffSessionAsync(user, credentialJson, ipHash, cancellationToken);

    public Task<Result<Unit>> ActivateRoleAsync(ClaimsPrincipal user, string code, string? ipHash, CancellationToken cancellationToken) =>
        staff.ActivateAsync(user, code, ipHash, cancellationToken);

    public Task<StaffSession> GetStaffSessionAsync(ClaimsPrincipal user, CancellationToken cancellationToken) =>
        staff.SessionAsync(user, cancellationToken);

    public Task<Result<string>> GetPasskeyRequestOptionsAsync(CancellationToken cancellationToken) =>
        passkeys.RequestOptionsAsync();

    public Task<Result<SignInOutcome>> SignInWithPasskeyAsync(string credentialJson, CancellationToken cancellationToken) =>
        passkeys.SignInAsync(credentialJson);

    public Task<Result<Unit>> StartPasskeySessionAsync(ClaimsPrincipal user, string credentialJson, string? ipHash, CancellationToken cancellationToken) =>
        staff.StartPasskeySessionAsync(user, credentialJson, ipHash, cancellationToken);

    public Task<Result<AccountSummary>> GetAccountAsync(ClaimsPrincipal user, CancellationToken cancellationToken) =>
        account.GetAsync(user, cancellationToken);

    public Task<IReadOnlyList<SessionSummary>> GetSessionsAsync(ClaimsPrincipal user, CancellationToken cancellationToken) =>
        account.SessionsAsync(user, cancellationToken);

    public Task<Result<Unit>> EndSessionAsync(ClaimsPrincipal user, Guid sessionId, CancellationToken cancellationToken) =>
        account.EndSessionAsync(user, sessionId, cancellationToken);

    public async Task<Result<IReadOnlyList<KnownBrowserSummary>>> GetKnownBrowsersAsync(ClaimsPrincipal user, CancellationToken cancellationToken) =>
        user.UserId() is { } id ? Result<IReadOnlyList<KnownBrowserSummary>>.Success(await browsers.ListAsync(id, cancellationToken)) : Error.Unauthorized("auth.required");

    public async Task<Result<Unit>> ForgetKnownBrowserAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken) =>
        user.UserId() is { } userId ? await browsers.ForgetAsync(userId, id, cancellationToken) : Error.Unauthorized("auth.required");

    public Task<Result<Unit>> GivePositionConsentAsync(ClaimsPrincipal user, string textVersion, CancellationToken cancellationToken) =>
        consents.GiveAsync(user, textVersion, cancellationToken);

    public Task<Result<Unit>> WithdrawPositionConsentAsync(ClaimsPrincipal user, CancellationToken cancellationToken) =>
        consents.WithdrawAsync(user, cancellationToken);

    public Task<Result<Unit>> AcceptTermsAsync(ClaimsPrincipal user, string textVersion, CancellationToken cancellationToken) =>
        terms.AcceptAsync(user, textVersion, cancellationToken);

    public Task<bool> TermsRequiredAsync(ClaimsPrincipal user, CancellationToken cancellationToken) =>
        terms.RequiredAsync(user, cancellationToken);

    public Task<Result<Unit>> HidePartyAsync(ClaimsPrincipal user, string? ipHash, CancellationToken cancellationToken) =>
        parties.HideAsync(user, ipHash, cancellationToken);

    public Task<Result<Unit>> WithdrawPartySupportAsync(ClaimsPrincipal user, string? ipHash, CancellationToken cancellationToken) =>
        parties.WithdrawAsync(user, ipHash, cancellationToken);

    public Task<Result<object>> ExportAsync(ClaimsPrincipal user, CancellationToken cancellationToken) => data.ExportAsync(user, cancellationToken);

    public Task<Result<Unit>> DeleteAccountAsync(ClaimsPrincipal user, CancellationToken cancellationToken) => data.DeleteAsync(user, cancellationToken);
}

/// <summary>Operational commands run by the migrator (seeding a trial invitation, granting a role).</summary>
public static class IdentityAdministration
{
    /// <summary>Creates the invitation if no invitation with that code exists. Returns whether one was created.</summary>
    public static async Task<bool> EnsureInvitationAsync(string connectionString, string code, int uses, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        await using var db = new IdentityDb(IdentityDb.StandaloneOptions(connectionString));
        // Compared as typed codes are (case, dashes and spaces do not matter), as the unique look-up column is.
        var normalized = Invitation.NormalizeCode(code);
        var lookup = InvitationRules.Lookup(normalized);
        if (await db.Invitations.AnyAsync(i => i.Lookup == lookup, cancellationToken))
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
}
