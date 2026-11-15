using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace Lt2.IntegrationTests.Infrastructure;

/// <summary>The web application with every log line, from Debug up, collected (EF Core's SQL included).</summary>
public sealed class LoggingFactory(DatabaseFixture database, CollectingSink sink) : Lt2Factory(database)
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Serilog:MinimumLevel:Default", "Debug");
        builder.UseSetting("Serilog:MinimumLevel:Override:Microsoft", "Debug");
        builder.UseSetting("Serilog:MinimumLevel:Override:Microsoft.AspNetCore", "Debug");
        builder.ConfigureServices(services => services.AddSingleton<ILogEventSink>(sink));
    }
}

/// <summary>Every Serilog event as one line: the rendered message, its properties and its exception.</summary>
public sealed class CollectingSink : ILogEventSink
{
    private readonly List<string> _lines = [];

    public List<string> Lines
    {
        get
        {
            lock (_lines)
            {
                return [.. _lines];
            }
        }
    }

    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        var properties = string.Join(" ", logEvent.Properties.Select(p => $"{p.Key}={p.Value}"));
        lock (_lines)
        {
            _lines.Add(logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture) + " " + properties + " " + logEvent.Exception);
        }
    }
}
