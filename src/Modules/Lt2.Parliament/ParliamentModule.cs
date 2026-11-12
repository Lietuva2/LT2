using Lt2.Parliament.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Lt2.Parliament;

/// <summary>Registration of the Parliament module. M0 ships only its schema and DbContext; the features come later.</summary>
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
        return services;
    }
}
