using System.Globalization;
using Lt2.Appeals;
using Lt2.Audit;
using Lt2.Identity;
using Lt2.Notifications;
using Lt2.Parliament;
using Lt2.SharedKernel;
using Lt2.SharedKernel.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Serilog;

namespace Lt2.Migrator;

/// <summary>
/// The migration steps, callable from the console entry point and from the integration tests:
/// <list type="number">
/// <item>with <c>ConnectionStrings:Admin</c> (optional): create the roles, set their passwords, hand the database to
/// <c>lt2_migrator</c>;</item>
/// <item>as <c>lt2_migrator</c> (<c>ConnectionStrings:Lt2</c>): create the schemas, apply every module's migrations,
/// grant access;</item>
/// <item>optional commands: <c>--seed-invitation CODE[:USES]</c>, <c>--grant-role ROLE EMAIL</c>.</item>
/// </list>
/// </summary>
public static class MigrationRunner
{
    public static async Task RunAsync(IConfiguration configuration, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(args);
        args = args.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();

        if (!string.IsNullOrEmpty(configuration.GetConnectionString("Admin")))
        {
            var adminConnection = ConnectionStrings.Build(configuration, "Admin", "Database:AdminPassword");
            await using var admin = await OpenWithRetryAsync(adminConnection, cancellationToken);
            await DatabaseRoles.BootstrapAsync(
                admin,
                new Dictionary<string, string?>
                {
                    [DatabaseRoles.Migrator] = configuration["Database:Password"],
                    [DatabaseRoles.App] = configuration["Database:AppPassword"],
                    [DatabaseRoles.Worker] = configuration["Database:WorkerPassword"],
                    [DatabaseRoles.Readonly] = configuration["Database:ReadonlyPassword"],
                },
                cancellationToken);
            Log.Information("Database roles are in place");
        }

        var connectionString = ConnectionStrings.Build(configuration);
        var databases = Databases();
        await using (var migrator = await OpenWithRetryAsync(connectionString, cancellationToken))
        {
            await DatabaseRoles.EnsureSchemasAsync(migrator, databases.SelectMany(d => d.Schemas), cancellationToken);
        }

        foreach (var database in databases)
        {
            Log.Information("Migrating {Module}", database.Module);
            await database.MigrateAsync(connectionString, cancellationToken);
        }

        await using (var migrator = await OpenWithRetryAsync(connectionString, cancellationToken))
        {
            await DatabaseRoles.GrantAsync(migrator, cancellationToken);
        }

        Log.Information("Migrations applied and grants in place");

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--seed-invitation" when i + 1 < args.Count:
                    var parts = args[++i].Split(':', 2);
                    var uses = parts.Length == 2 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 100;
                    var created = await IdentityAdministration.EnsureInvitationAsync(connectionString, parts[0], uses, cancellationToken);
                    Log.Information(created ? "Invitation created" : "Invitation already exists");
                    break;
                case "--grant-role" when i + 2 < args.Count:
                    var role = args[++i];
                    var email = args[++i];
                    var granted = await IdentityAdministration.GrantRoleAsync(connectionString, email, role, cancellationToken);
                    Log.Information(granted ? "Role {Role} granted" : "No account for that address; role {Role} not granted", role);
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete argument '{args[i]}'.", nameof(args));
            }
        }
    }

    /// <summary>Every module's database, in migration order.</summary>
    public static IReadOnlyList<IModuleDatabase> Databases()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton(NpgsqlDataSource.Create("Host=unused"));
        services.AddIdentityModule(configuration);
        services.AddNotificationsModule(configuration);
        services.AddAuditModule();
        services.AddParliamentModule();
        services.AddAppealsModule();
        using var provider = services.BuildServiceProvider();
        return provider.GetServices<IModuleDatabase>().ToList();
    }

    private static async Task<NpgsqlConnection> OpenWithRetryAsync(string connectionString, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var connection = new NpgsqlConnection(connectionString);
            try
            {
                await connection.OpenAsync(cancellationToken);
                return connection;
            }
            catch (NpgsqlException ex) when (attempt < 30 && ex.IsTransient)
            {
                await connection.DisposeAsync();
                Log.Information("Database not ready (attempt {Attempt}); retrying", attempt);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }
}
