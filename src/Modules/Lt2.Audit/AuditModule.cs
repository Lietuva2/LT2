using System.Text.Json;
using Lt2.Audit.Infrastructure;
using Lt2.Audit.Settings;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Lt2.Audit;

/// <summary>Registration of the Audit module: the append-only audit log and the runtime settings.</summary>
public static class AuditModule
{
    public const string AuditSchema = "audit";
    public const string ConfigSchema = "config";

    public static IServiceCollection AddAuditModule(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddDbContext<AuditDb>((sp, options) =>
            options.UseLt2Npgsql(sp.GetRequiredService<NpgsqlDataSource>(), AuditSchema));
        services.AddSingleton<IAuditLog, AuditLogWriter>();
        services.AddScoped<ISettings, SettingsReader>();
        services.AddScoped<ISettingsAdministration, SettingsAdministration>();
        services.AddSingleton<IModuleDatabase, AuditModuleDatabase>();
        return services;
    }
}

/// <summary>A runtime setting's current value.</summary>
public sealed record SettingValue(string Key, JsonElement Value, Guid? ChangedBy, DateTimeOffset? ChangedAt);

/// <summary>Reads and changes runtime settings on behalf of an administrator. Every change is audited.</summary>
public interface ISettingsAdministration
{
    Task<IReadOnlyList<SettingValue>> ListAsync(CancellationToken cancellationToken);

    Task<Result<SettingValue>> UpdateAsync(
        string key,
        JsonElement value,
        Guid actorUserId,
        string actorRole,
        string? ipHash,
        CancellationToken cancellationToken);
}
