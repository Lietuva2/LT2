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
/// Registration of the Appeals module (design 8): appeals, positions, arguments, endorsements, reports, moderation.
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
        services.AddScoped<AppealReads>();
        services.AddScoped<IAppealsModule, AppealsService>();
        services.AddScoped<IAppealsModeration, ModerationService>();
        services.AddScoped<IAppealsMaintenance, AppealsMaintenance>();
        services.AddScoped<IDigestSource, DigestSource>();
        return services;
    }
}
