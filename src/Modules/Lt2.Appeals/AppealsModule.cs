using Lt2.Appeals.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Lt2.Appeals;

/// <summary>Registration of the Appeals module. M0 ships only its schema and DbContext; the features come later.</summary>
public static class AppealsModule
{
    public const string Schema = "appeals";

    public static IServiceCollection AddAppealsModule(this IServiceCollection services)
    {
        services.AddDbContext<AppealsDb>((sp, options) =>
            options.UseNpgsql(
                    sp.GetRequiredService<NpgsqlDataSource>(),
                    o => o.MigrationsHistoryTable("__ef_migrations_history", Schema))
                .UseSnakeCaseNamingConvention());
        services.AddSingleton<IModuleDatabase, AppealsModuleDatabase>();
        return services;
    }
}
