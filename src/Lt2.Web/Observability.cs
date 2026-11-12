using System.Globalization;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;
using Serilog.Sinks.OpenTelemetry;

namespace Lt2.Web;

/// <summary>
/// Serilog to the console; OpenTelemetry traces, metrics and logs over OTLP only when an endpoint is configured
/// (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c>), since the backend is not chosen yet (design 9.4). No personal data is logged.
/// </summary>
internal static class Observability
{
    public static void AddLt2Observability(this WebApplicationBuilder builder, string serviceName)
    {
        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var json = !builder.Environment.IsDevelopment();

        // ReadFrom.Services: sinks registered as services are added too (the tests use one to read every log line).
        builder.Services.AddSerilog((services, logger) =>
        {
            logger.ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("service", serviceName);
            if (json)
            {
                logger.WriteTo.Console(new RenderedCompactJsonFormatter());
            }
            else
            {
                logger.WriteTo.Console(formatProvider: CultureInfo.InvariantCulture);
            }

            if (!string.IsNullOrEmpty(otlpEndpoint))
            {
                logger.WriteTo.OpenTelemetry(options =>
                {
                    options.Endpoint = otlpEndpoint;
                    options.Protocol = OtlpProtocol.Grpc;
                    options.ResourceAttributes = new Dictionary<string, object> { ["service.name"] = serviceName };
                });
            }
        });

        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName));
        telemetry.WithTracing(tracing =>
        {
            tracing.AddAspNetCoreInstrumentation(o => o.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation()
                .AddNpgsql();
            if (!string.IsNullOrEmpty(otlpEndpoint))
            {
                tracing.AddOtlpExporter();
            }
        });
        telemetry.WithMetrics(metrics =>
        {
            metrics.AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter("Npgsql")
                .AddMeter("Microsoft.AspNetCore.Identity");
            if (!string.IsNullOrEmpty(otlpEndpoint))
            {
                metrics.AddOtlpExporter();
            }
        });
    }
}
