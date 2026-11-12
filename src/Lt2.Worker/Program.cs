using System.Globalization;
using Hangfire;
using Hangfire.PostgreSql;
using Lt2.Audit;
using Lt2.Identity;
using Lt2.Notifications;
using Lt2.Parliament;
using Lt2.SharedKernel.Http;
using Lt2.SharedKernel.Persistence;
using Lt2.Worker;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

// Lt2.Worker: Hangfire server for scheduled and retried jobs, plus the outbox dispatcher (design 3, 9.1).
var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true, reloadOnChange: false);
var configuration = builder.Configuration;
var otlpEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

builder.Services.AddSerilog((_, logger) =>
{
    logger.ReadFrom.Configuration(configuration).Enrich.FromLogContext().Enrich.WithProperty("service", "lt2-worker");
    if (builder.Environment.IsDevelopment())
    {
        logger.WriteTo.Console(formatProvider: CultureInfo.InvariantCulture);
    }
    else
    {
        logger.WriteTo.Console(new RenderedCompactJsonFormatter());
    }

    if (!string.IsNullOrEmpty(otlpEndpoint))
    {
        logger.WriteTo.OpenTelemetry(options => options.Endpoint = otlpEndpoint);
    }
});
var telemetry = builder.Services.AddOpenTelemetry().ConfigureResource(r => r.AddService("lt2-worker"));
telemetry.WithTracing(t =>
{
    t.AddNpgsql();
    if (!string.IsNullOrEmpty(otlpEndpoint))
    {
        t.AddOtlpExporter();
    }
});
telemetry.WithMetrics(m =>
{
    m.AddMeter("Npgsql");
    if (!string.IsNullOrEmpty(otlpEndpoint))
    {
        m.AddOtlpExporter();
    }
});

var connectionString = ConnectionStrings.Build(configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(_ => new NpgsqlDataSourceBuilder(connectionString).Build());
builder.Services.AddNotificationsModule(configuration);
builder.Services.AddNotificationsDispatcher(configuration);
builder.Services.AddAuditModule();
builder.Services.AddIdentityMaintenance();
builder.Services.AddOutboundAllowlist(configuration);
builder.Services.AddParliamentModule();
builder.Services.AddParliamentImport(configuration);

builder.Services.AddHangfire(hangfire => hangfire
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(
        options => options.UseNpgsqlConnection(connectionString),
        new PostgreSqlStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = true }));
builder.Services.AddHangfireServer(options => options.WorkerCount = 2);
builder.Services.Configure<OutboxLoopOptions>(configuration.GetSection(OutboxLoopOptions.Section));
builder.Services.AddHostedService<OutboxDispatchLoop>();
builder.Services.AddHostedService<RecurringJobs>();

await builder.Build().RunAsync();
