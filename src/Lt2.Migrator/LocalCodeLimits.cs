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
/// one machine does not run out of codes (and, with <c>--official-addresses-without-invitation</c>, design 8.5.1's setting). The change goes through the settings module like an administrator's, written
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

    /// <summary>
    /// <c>--official-addresses-without-invitation</c> (design 8.5.1): switches <c>Registration:OfficialAddressesWithoutInvitation</c>
    /// on, through the settings module like an administrator's change (audited, actor role <c>migrator</c>), on a stack whose
    /// MPs are invented and whose email is caught: the local stack and the trial. Refused unless <c>Demo:Allowed</c> is true.
    /// Returns how many settings changed (0 when it was on already).
    /// </summary>
    public static async Task<int> OfficialAddressesWithoutInvitationAsync(IConfiguration configuration, string connectionString, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!configuration.GetValue<bool>("Demo:Allowed"))
        {
            throw new InvalidOperationException("Refused: --official-addresses-without-invitation runs only where Demo:Allowed is true (a stack with invented MPs).");
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddAuditModule();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsAdministration>();
        var key = SettingKeys.RegistrationOfficialAddressesWithoutInvitation.Name;
        var result = await settings.SetAsServerAsync(key, JsonSerializer.SerializeToElement(true), "migrator", cancellationToken);
        return result.IsSuccess ? (result.Value ? 1 : 0) : throw new InvalidOperationException($"{key}: true was refused ({result.Error!.Code}).");
    }
}
