using Lt2.Parliament.Import;
using Lt2.Parliament.Import.Client;
using Lt2.Parliament.Infrastructure;
using Lt2.Parliament.Statistics;
using Lt2.SharedKernel;
using Lt2.SharedKernel.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Lt2.Parliament;

/// <summary>Registration of the Parliament module: its schema and DbContext, and in the worker the open-data import.</summary>
public static class ParliamentModule
{
    public const string Schema = "parliament";

    public static IServiceCollection AddParliamentModule(this IServiceCollection services)
    {
        services.AddDbContext<ParliamentDb>((sp, options) =>
            options.UseNpgsql(
                    sp.GetRequiredService<NpgsqlDataSource>(),
                    o => o.MigrationsHistoryTable("__ef_migrations_history", Schema))
                .UseSnakeCaseNamingConvention());
        services.AddSingleton<IModuleDatabase, ParliamentModuleDatabase>();
        services.AddMemoryCache();
        services.AddScoped<IParliamentDataNotice, ParliamentDataNotice>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IParliamentQueries, ParliamentQueries>();
        return services;
    }

    /// <summary>
    /// The Seimas open-data client (worker only; the web host never calls the Seimas). Retries, time-outs and a circuit
    /// breaker come from <c>Microsoft.Extensions.Http.Resilience</c>; the host must be on <c>Outbound:AllowedHosts</c>.
    /// </summary>
    public static IServiceCollection AddParliamentImport(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(SeimasOptions.Section);
        var seimas = section.Get<SeimasOptions>() ?? new SeimasOptions();

        services.AddOutboundAllowlist(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<SeimasOptions>()
            .Bind(section)
            .Validate(o => o.BaseAddress is { IsAbsoluteUri: true } address && address.AbsolutePath.EndsWith('/'),
                "Seimas:BaseAddress must be an absolute address ending in a slash.")
            .ValidateOnStart();
        services.AddSingleton<SeimasThrottle>();
        services.AddHttpClient(SeimasClient.HttpClientName, (sp, http) =>
            {
                http.BaseAddress = sp.GetRequiredService<IOptions<SeimasOptions>>().Value.BaseAddress;
                http.Timeout = Timeout.InfiniteTimeSpan; // the resilience pipeline owns the time-outs
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Lietuva2.0-import/1.0");
            })
            .AddStandardResilienceHandler(o =>
            {
                var attempt = TimeSpan.FromSeconds(seimas.AttemptTimeoutSeconds);
                o.AttemptTimeout.Timeout = attempt;
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(Math.Max(seimas.TotalTimeoutSeconds, seimas.AttemptTimeoutSeconds));
                o.Retry.MaxRetryAttempts = Math.Max(1, seimas.RetryAttempts);
                o.Retry.Delay = TimeSpan.FromSeconds(seimas.RetryDelaySeconds);
                o.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
                o.Retry.UseJitter = true;
                o.CircuitBreaker.SamplingDuration = attempt * 2 + TimeSpan.FromSeconds(1);
            });
        services.AddTransient<ISeimasClient, SeimasClient>();
        services.AddScoped<IParliamentStatistics, ParliamentStatistics>();
        services.AddScoped<IParliamentImport, ParliamentImporter>();
        return services;
    }
}
