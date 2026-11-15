using System.Text.Json;
using Lt2.Audit;
using Lt2.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Lt2.Migrator;

/// <summary>
/// <c>--local-code-limits</c> (design 6.2, local stack only): raises the sign-in code limits per address
/// (<c>SignIn:CodesPerHour</c>, <c>SignIn:CodesPerDay</c>) to the highest values the settings allow, so hand testing on
/// one machine does not run out of codes. The change goes through the settings module like an administrator's, written
/// to <c>config.setting</c> and audited (actor role <c>migrator</c>). The defaults in code stay. It refuses unless
/// <c>Demo:Allowed</c> is true, which only <c>deploy/.env</c> sets.
/// </summary>
public static class LocalCodeLimits
{
    public const int PerHour = 20;
    public const int PerDay = 50;

    public static async Task<int> RunAsync(IConfiguration configuration, string connectionString, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!configuration.GetValue<bool>("Demo:Allowed"))
        {
            throw new InvalidOperationException("Refused: --local-code-limits runs only where Demo:Allowed is true (the local stack's deploy/.env).");
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddAuditModule();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsAdministration>();

        var changed = 0;
        foreach (var (key, value) in new[] { (SettingKeys.SignInCodesPerHour.Name, PerHour), (SettingKeys.SignInCodesPerDay.Name, PerDay) })
        {
            var result = await settings.SetAsServerAsync(key, JsonSerializer.SerializeToElement(value), "migrator", cancellationToken);
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException($"{key}: {value} was refused ({result.Error!.Code}).");
            }

            changed += result.Value ? 1 : 0;
        }

        return changed;
    }
}