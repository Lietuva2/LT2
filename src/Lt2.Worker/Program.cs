using System.Globalization;
using Hangfire;
using Hangfire.PostgreSql;
using Lt2.Appeals;
using Lt2.Audit;
using Lt2.Hosting;
using Lt2.Identity;
using Lt2.Notifications;
using Lt2.Parliament;
using Lt2.SharedKernel.Http;
using Lt2.SharedKernel.Persistence;
using Lt2.Worker;
using Npgsql;

// Lt2.Worker: Hangfire server for scheduled and retried jobs, plus the outbox dispatcher (design 3, 9.1).
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

builder.Services.AddHangfire(hangfire => hangfire
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(
        options => options.UseNpgsqlConnection(HangfireConnectionString(connectionString, configuration)),
        new PostgreSqlStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = true }));
builder.Services.AddHangfireServer(options => options.WorkerCount = 2);
builder.Services.Configure<OutboxLoopOptions>(configuration.GetSection(OutboxLoopOptions.Section));
builder.Services.AddHostedService<OutboxDispatchLoop>();
builder.Services.AddHostedService<RecurringJobs>();

await builder.Build().RunAsync();

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
