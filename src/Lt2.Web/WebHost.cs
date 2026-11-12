using System.Globalization;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;
using Lt2.Api.Contracts.Meta;
using Lt2.Appeals;
using Lt2.Audit;
using Lt2.Identity;
using Lt2.Notifications;
using Lt2.Parliament;
using Lt2.SharedKernel.Persistence;
using Lt2.Web.Client;
using Lt2.Web.Components;
using Lt2.Web.Endpoints;
using Lt2.Web.Security;
using Lt2.Web.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
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
        services.AddAppealsModule();
        RequireKeyEncryptionOutsideDevelopment(builder);

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
        services.AddScoped<IMetaApi, ServerMetaApi>();
        services.AddLt2ClientComponents();
    }

    public static void UseLt2Web(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseLt2SecurityHeaders();
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        // API errors become problem details; page errors show the Lithuanian error page.
        app.UseWhen(IsApi, api => api.UseExceptionHandler());
        app.UseWhen(context => !IsApi(context), pages => pages.UseExceptionHandler("/klaida", createScopeForErrors: true));
        app.UseStatusCodePages(ProblemStatusPages.WriteAsync);
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
        app.UseAuthorization();
        app.UseRateLimiter();
        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
        app.MapOpenApi("/openapi/{documentName}.json");
        app.MapLt2Api();
        app.MapRazorComponents<App>()
            .AddInteractiveWebAssemblyRenderMode()
            .AddAdditionalAssemblies(typeof(ClientAssembly).Assembly);
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

    internal static IServiceCollection AddForwardedHeadersFromConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            if (configuration.GetValue<bool>("ReverseProxy:TrustForwardedHeaders"))
            {
                // The web container is reachable only through Caddy on the internal network.
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            }
        });
        return services;
    }
}
