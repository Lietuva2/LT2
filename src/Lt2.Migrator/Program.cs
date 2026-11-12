using Lt2.Migrator;
using Microsoft.Extensions.Configuration;
using Serilog;

// Lt2.Migrator: run once per deploy, before the new web instances start (design 9.3).
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture)
    .CreateLogger();
try
{
    var configuration = new ConfigurationBuilder()
        .AddJsonFile("appsettings.json", optional: true)
        .AddEnvironmentVariables()
        .AddKeyPerFile("/run/secrets", optional: true)
        .Build();
    using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(10));
    await MigrationRunner.RunAsync(configuration, args, cancellation.Token);
    return 0;
}
#pragma warning disable CA1031 // The process exit code carries the failure.
catch (Exception ex)
#pragma warning restore CA1031
{
    Log.Fatal(ex, "Migration failed");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
