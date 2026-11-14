using Lt2.Appeals.Application;
using Lt2.Appeals.Infrastructure;
using Lt2.Appeals.Screening;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Lt2.Appeals;

/// <summary>
/// Registration of the Appeals module (design 8, 8.12): bills' records, positions, arguments, endorsements, reports, moderation.
/// Needs <see cref="IParticipants"/> (Identity), <see cref="IBillCatalogue"/> (Parliament), <see cref="ISettings"/>,
/// <see cref="IAuditLog"/> (Audit) and <see cref="IOutbox"/> (Notifications) from the host.
/// </summary>
public static class AppealsModule
{
    public const string Schema = "appeals";

    public static IServiceCollection AddAppealsModule(this IServiceCollection services)
    {
        // A context per operation: a page renders several components at once, and they must not share one context.
        services.AddDbContextFactory<AppealsDb>((sp, options) =>
            options.UseNpgsql(
                    sp.GetRequiredService<NpgsqlDataSource>(),
                    o => o.MigrationsHistoryTable("__ef_migrations_history", Schema))
                .UseSnakeCaseNamingConvention());
        services.AddSingleton<IModuleDatabase, AppealsModuleDatabase>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPositionErasure, PositionErasure>();
        services.AddSingleton<IPreScreening, RuleBasedPreScreening>();

        // Design 8.11.4: the "not configured" assistant unless AddAiAssistant registered the Claude one first.
        services.TryAddSingleton<IAiAssistant, Ai.NotConfiguredAiAssistant>();

        // Design 8.13: no reader of the bills' official texts exists yet (e-seimas, design 7.1), so the summary job makes nothing.
        services.TryAddSingleton<IBillTextSource, Ai.UnavailableBillTextSource>();
        services.AddSingleton<SummaryJobNotices>();
        services.AddScoped<IDescriptionSummaries, DescriptionSummaries>();
        services.AddScoped<AppealReads>();
        services.AddScoped<BillRecords>();
        services.AddScoped<IAppealsModule, AppealsService>();
        services.AddScoped<IAppealsModeration, ModerationService>();
        services.AddScoped<IAppealsMaintenance, AppealsMaintenance>();
        services.AddScoped<IDigestSource, DigestSource>();
        services.AddScoped<IAccountData, AccountData>();
        return services;
    }

    /// <summary>
    /// The Claude assistant for the description draft (design 8.11.4), registered only when <c>Ai:Provider</c> is
    /// <c>claude</c> and <c>Ai:ApiKey</c> is set; otherwise the "not configured" one stays and no draft is offered. Call before
    /// <see cref="AddAppealsModule"/>. The caller adds the provider's host to the outbound allowlist.
    /// </summary>
    public static IServiceCollection AddAiAssistant(this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var key = configuration[Ai.AiConfiguration.ApiKey]?.Trim();
        if (!Ai.AiConfiguration.IsClaude(configuration[Ai.AiConfiguration.Provider], key))
        {
            return services;
        }

        services.AddHttpClient(Ai.AiConfiguration.HttpClientName, c => c.Timeout = TimeSpan.FromMinutes(5));
        services.AddSingleton<IAiAssistant>(sp => new Ai.ClaudeAiAssistant(
            sp.GetRequiredService<IHttpClientFactory>(),
            key!,
            new ScopedSettings(sp.GetRequiredService<IServiceScopeFactory>()),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Ai.ClaudeAiAssistant>>()));
        return services;
    }

    /// <summary>The AI provider's host when the provider is configured (design 8.11.4), for the outbound allowlist; otherwise null.</summary>
    public static string? AiProviderHost(Microsoft.Extensions.Configuration.IConfiguration configuration) =>
        Ai.AiConfiguration.IsClaude(configuration?[Ai.AiConfiguration.Provider], configuration?[Ai.AiConfiguration.ApiKey]) ? Ai.AiConfiguration.ClaudeHost : null;

    /// <summary>Reads a setting in a scope of its own: the settings reader is scoped, the assistant a singleton.</summary>
    private sealed class ScopedSettings(IServiceScopeFactory scopes) : ISettings
    {
        public async Task<T> GetAsync<T>(SettingKey<T> key, CancellationToken cancellationToken = default)
        {
            await using var scope = scopes.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ISettings>().GetAsync(key, cancellationToken);
        }
    }
}
