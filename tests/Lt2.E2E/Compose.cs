using System.Diagnostics;

namespace Lt2.E2E;

/// <summary>Runs <c>docker compose</c> against the stack under test (the docker CLI must be on the path).</summary>
internal static class Compose
{
    public static string File =>
        Environment.GetEnvironmentVariable("LT2_E2E_COMPOSE_FILE") ?? Path.Combine(RepositoryRoot(), "deploy", "compose.yaml");

    /// <summary>
    /// One compose command at a time. The classes now run one after another (xunit.runner.json); the gate stays in case
    /// parallel runs come back: two migrator runs at once (each staff fixture grants its role through the migrator, which
    /// also re-applies grants) collide in PostgreSQL ("tuple concurrently updated").
    /// </summary>
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);

    /// <summary>Runs <c>docker compose -f FILE ARGS…</c> and returns its standard output; fails on a non-zero exit.</summary>
    public static async Task<string> RunAsync(params string[] arguments)
    {
        await OneAtATime.WaitAsync();
        try
        {
            return await RunOnceAsync(arguments);
        }
        finally
        {
            OneAtATime.Release();
        }
    }

    private static async Task<string> RunOnceAsync(string[] arguments)
    {
        var start = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "compose", "-f", File }.Concat(arguments))
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        // The migrator logs to standard output, so a failure shows both.
        process.ExitCode.ShouldBe(0, $"{await errors}{Environment.NewLine}{await output}");
        return await output;
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "LT2.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("LT2.sln not found; set LT2_E2E_COMPOSE_FILE.");
    }
}
