using System.Globalization;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;
using Lt2.Api.Contracts.Meta;
using Lt2.Api.Contracts.Parliament;
using Lt2.Appeals;
using Lt2.SharedKernel.Http;
using Lt2.Audit;
using Lt2.Hosting;
using Lt2.Identity;
using Lt2.Notifications;
using Lt2.Parliament;
using Lt2.Usage;
using Lt2.SharedKernel.Persistence;
using Lt2.Web.Client;
using Lt2.Web.Components;
using Lt2.Web.Components.Layout;
using Lt2.Web.Components.Members;
using Lt2.Web.Endpoints;
using Lt2.Web.Jobs;
using Lt2.Web.Security;
using Lt2.Web.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Localization;
using Npgsql;
using Serilog;

namespace Lt2.Web;

/// <summary>Composition of the web host: modules, security, API, Blazor and observability.</summary>
internal static class WebHost
{
    public static void AddLt2Web(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        configuration.AddKeyPerFile("/run/secrets", optional: true, reloadOnChange: false);

        builder.AddLt2Observability("lt2-web");

        var services = builder.Services;
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(_ => new NpgsqlDataSourceBuilder(ConnectionStrings.Build(configuration)).Build());

        // Modules.
        services.AddIdentityModule(configuration);
        services.AddNotificationsModule(configuration);
        services.AddAuditModule();
        services.AddParliamentModule();

        // Design 8.11.4: the AI draft through the Claude API only when configured (provider and key); AddAiAssistant then
        // adds its host, and only it, to the outbound allowlist of the web host's HTTP clients (design 6.4).
        services.AddOutboundAllowlist(configuration);
        services.AddAiAssistant(configuration);
        services.AddAppealsModule();
        services.AddUsageModule();
        services.AddUsageCounting();
        RequireKeyEncryptionOutsideDevelopment(builder);

        // Design 3, 6.3: the administrators' Hangfire dashboard over the worker's storage; no Hangfire server runs here.
        services.AddLt2JobsDashboard();

        // Security plumbing (design 6.4).
        services.AddLt2Security(configuration);

        // API.
        services.AddProblemDetails();
        services.AddOpenApi("v1");
        services.AddLocalization();
        services.AddLt2Validators();
        services.AddScoped<ErrorMapper>();
        services.AddSingleton<IpHasher>();
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        // Blazor: static server rendering for public pages, Interactive WebAssembly for signed-in features.
        services.AddRazorComponents()
            .AddInteractiveWebAssemblyComponents()
            .AddAuthenticationStateSerialization();
        services.AddCascadingAuthenticationState();
        services.AddHttpContextAccessor();
        services.AddScoped<IAuthApi, ServerAuthApi>();
        services.AddScoped<IMeApi, ServerMeApi>();
        services.AddScoped<Lt2.Api.Contracts.Mps.IMpApi, ServerMpApi>();
        services.AddScoped<MpResponseReads>();
        services.AddScoped<Lt2.Api.Contracts.Mps.IMpResponsesApi, ServerMpResponsesApi>();
        services.AddScoped<Lt2.Api.Contracts.Mps.IMpResponseModerationApi, ServerMpResponseModerationApi>();
        services.AddScoped<CommentReads>();
        services.AddScoped<Lt2.Api.Contracts.Comments.ICommentsApi, ServerCommentsApi>();
        services.AddScoped<Lt2.Api.Contracts.Comments.ICommentModerationApi, ServerCommentModerationApi>();
        services.AddScoped<ParliamentReads>();
        services.AddScoped<IMetaApi, ServerMetaApi>();
        services.AddScoped<IParliamentApi, ServerParliamentApi>();
        services.AddScoped<IAppealsApi, ServerAppealsApi>();
        services.AddScoped<IModerationApi, ServerModerationApi>();
        services.AddScoped<Lt2.Api.Contracts.Organisations.IOrganisationsApi, ServerOrganisationsApi>();
        services.AddScoped<Lt2.Api.Contracts.Organisations.IOrganisationModerationApi, ServerOrganisationModerationApi>();
        services.AddScoped<AgendaReads>();
        services.AddScoped<Lt2.Api.Contracts.Agenda.IAgendaApi, ServerAgendaApi>();
        services.AddScoped<Lt2.Api.Contracts.Follows.IFollowsApi, ServerFollowsApi>();
        services.AddScoped<Lt2.Api.Contracts.Follows.INoticeFrequencyApi, ServerNoticeFrequencyApi>();
        services.AddScoped<Lt2.Api.Contracts.Follows.INoticeStopApi, ServerNoticeStopApi>();

        // Design 8.25: public profiles, the page composed from Identity, Notifications and Appeals.
        services.AddScoped<MemberReads>();
        services.AddScoped<MyProfileForms>();
        services.AddScoped<Lt2.Api.Contracts.Me.IProfilesApi, ServerProfilesApi>();
        services.AddScoped<MyBillsReads>();
        services.AddScoped<TakingSides>();
        services.AddScoped<Lt2.Api.Contracts.Me.IMyBillsApi, ServerMyBillsApi>();
        services.AddSingleton<AnalyticsCopies>();
        services.AddScoped<AnalyticsReads>();
        services.AddScoped<MyAgreementReads>();
        services.AddScoped<Lt2.Api.Contracts.Analytics.IAnalyticsApi, ServerAnalyticsApi>();
        services.AddScoped<FrontPageAgenda>();
        services.AddSingleton<FrontPageFreshness>();
        services.AddSingleton<FeedbackLimiter>();
        services.AddScoped<Lt2.Api.Contracts.Feedback.IFeedbackApi, ServerFeedbackApi>();
        services.AddScoped<IAdminApi, ServerAdminApi>();
        services.AddScoped<Lt2.Api.Contracts.Admin.IUsageApi, ServerUsageApi>();
        services.AddScoped<Lt2.Api.Contracts.Digests.IDigestsApi, ServerDigestsApi>();
        services.AddScoped<MpMailHost>();
        services.AddScoped<Lt2.Api.Contracts.Digests.IMpMailApi, ServerMpMailApi>();
        services.AddScoped<Lt2.Api.Contracts.Digests.IMpSendingAdminApi, ServerMpSendingAdminApi>();
        services.AddScoped<Lt2.Web.Components.Parliament.Formats>();
        services.AddLt2ClientComponents();
    }

    public static void UseLt2Web(this WebApplication app)
    {
        // Design 8.20.2: without the key of the known browsers' hashes no browser is known, and sign-in works as for a stranger.
        if (string.IsNullOrWhiteSpace(app.Configuration["Security:DeviceHashKey"]))
        {
            app.Logger.LogWarning("Security:DeviceHashKey is not set (deploy/secrets/device_hash_key): no browser is known to an account");
        }

        TrustedProxy.UseTrustedForwardedHeaders(app);
        app.UseLt2SecurityHeaders();
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        // API errors become problem details; page errors show the Lithuanian error page.
        app.UseWhen(IsApi, api => api.UseExceptionHandler());
        app.UseWhen(context => !IsApi(context), pages => pages.UseExceptionHandler("/klaida", createScopeForErrors: true));
        app.UseWhen(IsApi, api => api.UseStatusCodePages(ProblemStatusPages.WriteAsync));

        // An address no page answers gets the site's own "not found" page, with the layout and its notices. Not the Hangfire
        // dashboard: its script reads its own answers (a refused form is a bare 403, not a page).
        app.UseWhen(
            context => !IsApi(context) && !JobsDashboard.IsDashboard(context.Request.Path),
            pages => pages.UseStatusCodePagesWithReExecute("/nerasta", createScopeForStatusCodePages: true));

        // Routing after the status-code pages, so the re-executed "/nerasta" request is routed to its page.
        app.UseRouting();

        // The layout's invented-data notice, read before a page renders: an error or a re-executed status page renders in one
        // pass and cannot wait for it (Components/Layout/InventedDataNotice).
        app.UseInventedDataNotice();

        // Design 8.25.2 (owner, 2026-11-16): a member's page answers only at its current address; any other name part redirects.
        app.UseMemberAddresses();

        // Design 8.19.4: the path of an MP's link holds its token; every log line keeps the path without it (Lt2.Hosting's
        // SecretPaths, applied to all log events and to the traces).
        app.UseSerilogRequestLogging();

        var lithuanian = new CultureInfo("lt-LT");
        app.UseRequestLocalization(new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture(lithuanian),
            SupportedCultures = [lithuanian],
            SupportedUICultures = [lithuanian],
            RequestCultureProviders = [],
        });

        app.UseAuthentication();

        // Design 8.27: the site's own usage statistics, after authentication so a signed-in session counts its account. It
        // looks at a request only when it is answered; static files never reach it (they are answered by routing).
        app.UseUsageCounting();

        // Design 8.22.2: a member who has not accepted the terms in force may write nothing but the exempt routes; before
        // authorization, so the refusal is the same whatever else the route requires.
        app.UseMiddleware<TermsGate>();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.UseAntiforgery();

        // Static files (the WebAssembly runtime, styles, scripts, images) are answered by the routing step itself, before
        // authentication: a signed-in browser's dozens of parallel downloads must not each look up the session in the
        // database (design 9.5). They need no person; pages and the API still check the session on every request, so an
        // ended session stops working at once.
        app.MapStaticAssets().ShortCircuit();
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
        app.MapOpenApi("/openapi/{documentName}.json");
        app.MapLt2Api();
        app.MapLt2JobsDashboard();
        MapOldAppealAddresses(app);
        app.MapRazorComponents<App>()
            .AddInteractiveWebAssemblyRenderMode()
            .AddAdditionalAssemblies(typeof(ClientAssembly).Assembly);
    }

    /// <summary>
    /// Design 8.12.1: the bill and the record are one page. The old record addresses (in emails, summaries and bookmarks)
    /// redirect permanently: <c>/seimas/{number}/kreipimasis</c> to the bill's page, its history to
    /// <c>/seimas/{number}/istorija</c>, and the proposal form <c>/kreipimaisi/naujas</c> to the list. The list itself moved
    /// from <c>/kreipimaisi</c> to <c>/projektai</c> (design 8.12.5); its old address keeps the query.
    /// </summary>
    private static void MapOldAppealAddresses(IEndpointRouteBuilder app)
    {
        static string Bill(string number) => "/seimas/" + Uri.EscapeDataString(number);
        app.MapGet("/seimas/{number}/kreipimasis", (string number) => Results.Redirect(Bill(number), permanent: true)).ExcludeFromDescription();
        app.MapGet("/seimas/{number}/kreipimasis/istorija", (string number) => Results.Redirect(Bill(number) + "/istorija", permanent: true)).ExcludeFromDescription();
        app.MapGet("/kreipimaisi/naujas", () => Results.Redirect("/projektai", permanent: true)).ExcludeFromDescription();
        app.MapGet("/kreipimaisi", (HttpRequest request) => Results.Redirect("/projektai" + request.QueryString, permanent: true)).ExcludeFromDescription();
    }

    private static bool IsApi(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);

    private static void RequireKeyEncryptionOutsideDevelopment(WebApplicationBuilder builder)
    {
        var configured = !string.IsNullOrEmpty(builder.Configuration["DataProtection:KeyEncryptionKey"]);
        if (!configured && !builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException(
                "DataProtection:KeyEncryptionKey must be supplied (as a secret file) outside development.");
        }
    }
}
