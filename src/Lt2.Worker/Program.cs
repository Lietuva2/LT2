using System.Globalization;
using Hangfire;
using Hangfire.PostgreSql;
using Lt2.Appeals;
using Lt2.Audit;
using Lt2.Hosting;
using Lt2.Identity;
using Lt2.Jobs;
using Lt2.Notifications;
using Lt2.Parliament;
using Lt2.SharedKernel;
using Lt2.SharedKernel.Http;
using Lt2.SharedKernel.Persistence;
using Lt2.Worker;
using Npgsql;

// Lt2.Worker: Hangfire server for scheduled and retried jobs, plus the outbox dispatcher (design 3, 9.1).
// "jobs list" and "jobs trigger {id}" instead list or trigger the recurring jobs and exit, with no server (design 6.3).
if (JobsCommand.Matches(args))
{
    return await RunJobsCommandAsync(args);
}

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true, reloadOnChange: false);
var configuration = builder.Configuration;
builder.AddLt2Observability("lt2-worker");

var connectionString = ConnectionStrings.Build(configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(_ => new NpgsqlDataSourceBuilder(connectionString).Build());
builder.Services.AddNotificationsModule(configuration);
builder.Services.AddNotificationsDispatcher(configuration);
builder.Services.AddAuditModule();
builder.Services.AddIdentityMaintenance();

// Design 8.13: the summary job asks the Claude API only when configured (Ai:Provider and the key file), as the web host
// does for the draft (8.11.4); AddAiAssistant then adds its host, and only it, to the outbound allowlist (design 6.4).
builder.Services.AddOutboundAllowlist(configuration);
builder.Services.AddParliamentModule();
builder.Services.AddParliamentImport(configuration);
builder.Services.AddIdentityDirectory();
builder.Services.AddAiAssistant(configuration);
builder.Services.AddAppealsModule();

// A job runs as long as it needs: the sliding invisibility timeout keeps a long one (the first import, or a forced full
// re-read by hand, design 7.2) from being fetched a second time by the other worker after the 30 minutes a fetched job
// otherwise stays invisible.
builder.Services.AddHangfire(hangfire => UseHangfireStorage(hangfire, connectionString, configuration, prepareSchema: true));
builder.Services.AddHangfireServer(options => options.WorkerCount = 2);
builder.Services.Configure<OutboxLoopOptions>(configuration.GetSection(OutboxLoopOptions.Section));
builder.Services.AddHostedService<OutboxDispatchLoop>();
builder.Services.AddHostedService<RecurringJobs>();

await builder.Build().RunAsync();
return 0;

// The worker's Hangfire storage; the jobs command uses it too, without installing or upgrading the schema.
static void UseHangfireStorage(IGlobalConfiguration hangfire, string connectionString, IConfiguration configuration, bool prepareSchema) => hangfire
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(
        options => options.UseNpgsqlConnection(HangfireConnectionString(connectionString, configuration)),
        new PostgreSqlStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = prepareSchema, UseSlidingInvisibilityTimeout = true });

// Design 6.3: the jobs command, with the worker's configuration, database and audit log, and no Hangfire server.
static async Task<int> RunJobsCommandAsync(string[] args)
{
    var builder = Host.CreateApplicationBuilder();
    builder.Configuration.AddKeyPerFile("/run/secrets", optional: true, reloadOnChange: false);
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
    var configuration = builder.Configuration;
    var connectionString = ConnectionStrings.Build(configuration);
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton(_ => new NpgsqlDataSourceBuilder(connectionString).Build());
    builder.Services.AddAuditModule();
    builder.Services.AddHangfire(hangfire => UseHangfireStorage(hangfire, connectionString, configuration, prepareSchema: false));

    using var host = builder.Build();
    return await JobsCommand.RunAsync(
        args,
        host.Services.GetRequiredService<JobStorage>(),
        host.Services.GetRequiredService<IAuditLog>(),
        Console.Out,
        CancellationToken.None);
}

// Hangfire keeps its own pool (Npgsql pools per connection string), so it gets its own size and name in the budget of
// design 9.5 rather than a second copy of the worker's.
static string HangfireConnectionString(string connectionString, IConfiguration configuration) =>
    new NpgsqlConnectionStringBuilder(connectionString)
    {
        MaxPoolSize = configuration["Database:HangfireMaxPoolSize"] is { Length: > 0 } size
            ? int.Parse(size, CultureInfo.InvariantCulture)
            : ConnectionStrings.DefaultMaxPoolSize,
        ApplicationName = "lt2-hangfire",
    }.ConnectionString;
