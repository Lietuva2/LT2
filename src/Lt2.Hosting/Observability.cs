using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;
using Serilog.Sinks.OpenTelemetry;

namespace Lt2.Hosting;

/// <summary>
/// The one logging and telemetry set-up of both hosts: Serilog to the console (compact JSON outside development);
/// OpenTelemetry traces, metrics and logs over OTLP only when an endpoint is configured (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c>),
/// since the backend is not chosen yet (design 9.4). Outbound HTTP calls (the Seimas, lrs.lt, the AI provider) are traced
/// in both. No personal data is logged.
/// </summary>
public static class Observability
{
    public static void AddLt2Observability(this IHostApplicationBuilder builder, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var configuration = builder.Configuration;
        var otlpEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var json = !builder.Environment.IsDevelopment();
        var web = builder is Microsoft.AspNetCore.Builder.WebApplicationBuilder;

        // ReadFrom.Services: sinks registered as services are added too (the tests use one to read every log line).
        builder.Services.AddSerilog((services, logger) =>
        {
            logger.ReadFrom.Configuration(configuration)
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
            if (web)
            {
                tracing.AddAspNetCoreInstrumentation(o => o.Filter = context => !context.Request.Path.StartsWithSegments("/health"));
            }

            tracing.AddHttpClientInstrumentation().AddNpgsql();
            if (!string.IsNullOrEmpty(otlpEndpoint))
            {
                tracing.AddOtlpExporter();
            }
        });
        telemetry.WithMetrics(metrics =>
        {
            if (web)
            {
                metrics.AddAspNetCoreInstrumentation().AddMeter("Microsoft.AspNetCore.Identity");
            }

            metrics.AddHttpClientInstrumentation().AddMeter("Npgsql");
            if (!string.IsNullOrEmpty(otlpEndpoint))
            {
                metrics.AddOtlpExporter();
            }
        });
    }
}