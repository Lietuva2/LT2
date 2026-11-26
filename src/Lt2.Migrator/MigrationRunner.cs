using System.Globalization;
using Lt2.Appeals;
using Lt2.Audit;
using Lt2.Usage;
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
/// <item>with <c>ConnectionStrings:Admin</c> (optional): let <c>lt2_app</c> read and write the worker's Hangfire tables (the
/// administrators' dashboard);</item>
/// <item>optional commands: <c>--seed-invitation CODE[:USES]</c>, <c>--grant-role ROLE EMAIL</c> (prints the one-time
/// activation code on the console, never to a log sink), <c>--reset-parliament</c> (empties the parliament schema,
/// refusing unless every import so far came from the Seimas stand-in), <c>--local-code-limits</c> (local stack only: the code limits per address raised to the settings' highest values, refusing unless <c>Demo:Allowed</c> is set), <c>--official-addresses-without-invitation</c> (a stack whose MPs are invented, refusing unless <c>Demo:Allowed</c> is set: design 8.5.1's setting switched on), <c>--demo-appeal</c> (local demonstration data:
/// invented participants on XVP-0412 and the invented closed bills, refusing unless <c>Demo:Allowed</c> is set and the
/// parliament data is invented), <c>--demo-sides-for ADDRESS|-</c> (one existing account's random sides on the invented
/// closed bills, under the same refusals and only with that account's positions consent; the address is never logged),
/// <c>--demo-activity</c> (the trial: invented members' activity on the real Seimas data, refusing unless <c>Demo:Allowed</c>
/// and <c>Email:Caught</c> are set and the parliament data is real only; design 8.26.5),
/// <c>--demo-emails</c> (one email of every outbox kind and variant, invented, to <c>NAME@pavyzdys.test</c>, so Mailpit shows how each looks; refusing unless <c>Demo:Allowed</c> and <c>Email:Caught</c> are set; writes outbox rows only).</item>
/// </list>
/// </summary>
public static class MigrationRunner
{
    /// <summary>
    /// The PostgreSQL advisory lock every migrator run holds from its first step to its last, so two copies started at
    /// once (a deploy and a hand run, or two test fixtures) take turns instead of changing roles and grants together
    /// ("tuple concurrently updated"). The number is arbitrary and used for nothing else.
    /// </summary>
    public const long LockKey = 0x4C54_325F_4D49_4752; // "LT2_MIGR"

    public static async Task RunAsync(IConfiguration configuration, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(args);
        args = args.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();

        // Advisory locks belong to the database, whichever role takes them: the administrator's connection when there is
        // one (it runs the first step), else the migrator's. A session lock outlives a connection handed back to a pool,
        // so this connection is not pooled: closing it ends the session and the lock, also if the run fails.
        var lockConnection = new NpgsqlConnectionStringBuilder(!string.IsNullOrEmpty(configuration.GetConnectionString("Admin"))
            ? ConnectionStrings.Build(configuration, "Admin", "Database:AdminPassword")
            : ConnectionStrings.Build(configuration))
        {
            Pooling = false,
        }.ConnectionString;
        await using var held = await OpenWithRetryAsync(lockConnection, cancellationToken);
        await using (var tryLock = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", held))
        {
            tryLock.Parameters.AddWithValue("key", LockKey);
            if (await tryLock.ExecuteScalarAsync(cancellationToken) is not true)
            {
                Log.Information("Another migrator run is in progress; waiting for it to finish");
                await using var wait = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", held) { CommandTimeout = 0 };
                wait.Parameters.AddWithValue("key", LockKey);
                await wait.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        try
        {
            await RunLockedAsync(configuration, args, cancellationToken);
        }
        finally
        {
            await using var unlock = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", held);
            unlock.Parameters.AddWithValue("key", LockKey);
            await unlock.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private static async Task RunLockedAsync(IConfiguration configuration, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
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

        // The worker owns the Hangfire tables, so only the administrator can let the web host's dashboard read and write them.
        // Without the administrator's connection the grants of the last run stay (default privileges cover tables made since).
        if (!string.IsNullOrEmpty(configuration.GetConnectionString("Admin")))
        {
            await using var admin = await OpenWithRetryAsync(ConnectionStrings.Build(configuration, "Admin", "Database:AdminPassword"), cancellationToken);
            await DatabaseRoles.GrantHangfireToAppAsync(admin, cancellationToken);
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
                    var grant = await GrantRoleAsync(connectionString, email, role, cancellationToken);
                    if (grant.IsSuccess)
                    {
                        // The activation code goes to the console once, never to a log sink or an email (design 6.2).
                        Log.Information("Role {Role} granted; the activation code follows on the console only", role);
                        await Console.Out.WriteLineAsync(string.Create(
                            CultureInfo.InvariantCulture,
                            $"Activation code for the {role} role (valid until {grant.Value.ExpiresAt:yyyy-MM-dd HH:mm} UTC, shown once): {grant.Value.ActivationCode}"));
                        await Console.Out.FlushAsync(cancellationToken);
                    }
                    else
                    {
                        Log.Warning("Role {Role} not granted: {Reason}", role, grant.Error!.Code);
                    }

                    break;
                case "--reset-parliament":
                    var reset = await ParliamentMaintenance.ResetInventedDataAsync(connectionString, cancellationToken);
                    if (!reset.IsSuccess)
                    {
                        // Real Seimas data is never deleted this way; the exit code tells a script it was refused.
                        throw new InvalidOperationException(reset.Error!.Code == "parliament.reset.real-data"
                            ? "Refused: the parliament schema holds data imported from the real Seimas service; --reset-parliament empties invented data only."
                            : "Refused: a parliament import is running; try again when it has finished.");
                    }

                    Log.Information(
                        "Parliament data emptied: {Runs} import runs, {Documents} stored responses, {People} people, {Votes} votes (all invented)",
                        reset.Value.ImportRuns, reset.Value.RawDocuments, reset.Value.People, reset.Value.Votes);
                    break;
                case "--demo-appeal":
                    var demo = await DemoAppeal.RunAsync(configuration, connectionString, TimeProvider.System, cancellationToken);
                    Log.Information(
                        "Demonstration record on {Bill}: {Status}; invented accounts created: {Created}; AI description on {AiBill}: {AiStatus}; invented organisations created: {Organisations}; invented accounts that accepted the terms of use now: {Terms}; invented accounts that gave the positions consent's newest text now: {Consents}; closed demonstration bill {ClosedBill}: {Closed}; invented closed bills: {ClosedBills}",
                        DemoAppeal.Bill, demo.Status, demo.AccountsCreated, DemoAppeal.AiBill, demo.AiDescription, demo.Organisations, demo.TermsAccepted, demo.PositionConsentsGiven, DemoAppeal.ClosedBill, demo.ClosedBill, demo.ClosedBills);
                    break;
                case "--demo-activity":
                    var activity = await DemoActivity.RunAsync(configuration, connectionString, TimeProvider.System, cancellationToken);
                    Log.Information("Invented members' activity on the real Seimas data: {Outcome}", activity.ToString());
                    break;
                case "--demo-sides-for" when i + 1 < args.Count:
                    // The address is never logged: only what was given (design 8.26.5).
                    var sides = await DemoSides.RunAsync(configuration, connectionString, args[++i], TimeProvider.System, cancellationToken);
                    Log.Information(
                        "Demonstration sides for the named account: {For} for, {Against} against, given now on the invented closed bills",
                        sides.For, sides.Against);
                    break;
                case "--demo-emails":
                    var queued = await DemoEmails.EnqueueAsync(configuration, connectionString, TimeProvider.System, cancellationToken);
                    Log.Information("Demonstration emails queued: {Count}, to NAME@{Domain}; the worker sends them to Mailpit", queued, DemoEmails.Domain);
                    break;
                case "--local-code-limits":
                    var raised = await LocalCodeLimits.RunAsync(configuration, connectionString, cancellationToken);
                    Log.Information("Local sign-in code limits: {PerHour} an hour, {PerDay} a day ({Changed} settings changed)", LocalCodeLimits.PerHour, LocalCodeLimits.PerDay, raised);
                    break;
                case "--official-addresses-without-invitation":
                    var switched = await LocalCodeLimits.OfficialAddressesWithoutInvitationAsync(configuration, connectionString, cancellationToken);
                    Log.Information("Registration with an MP's official address without an invitation: on ({Changed} settings changed)", switched);
                    break;
                default:
                    // An address passed by mistake is not echoed (design 6.8: no addresses in logs).
                    throw new ArgumentException(
                        args[i].Contains('@', StringComparison.Ordinal) ? "Unknown argument (an address; not shown)." : $"Unknown or incomplete argument '{args[i]}'.",
                        nameof(args));
            }
        }
    }

    /// <summary>
    /// Grants a staff role from the server, for the first administrator or a lost last one (design 6.2): the grant and
    /// its notice email are written like any other, and the one-time activation code is returned to the caller.
    /// </summary>
    public static async Task<Result<RoleGrant>> GrantRoleAsync(string connectionString, string email, string role, CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddNotificationsModule(new ConfigurationBuilder().Build());
        services.AddAuditModule();
        services.AddIdentityAdministration();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IIdentityAdministration>()
            .GrantRoleAsync(email, role, Actor.Server, cancellationToken);
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
        services.AddUsageModule();
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
