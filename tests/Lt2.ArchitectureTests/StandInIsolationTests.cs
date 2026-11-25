using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lt2.ArchitectureTests;

/// <summary>
/// The Seimas stand-in (tools/Lt2.SeimasStandIn) answers with invented data. It must be impossible to ship: no product
/// project references it, no production image contains it, no production configuration names it, and compose runs it
/// only in the "local" profile.
/// </summary>
public sealed class StandInIsolationTests
{
    private const string StandInProject = "Lt2.SeimasStandIn";
    private const string StandInService = "seimas-standin";
    private static readonly Regex StandInWord = new("stand-?in|seimas-standin|SeimasStandIn", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Fact]
    public void No_project_under_src_references_the_stand_in()
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains(StandInProject, StringComparison.OrdinalIgnoreCase))
            .ToList();
        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void The_production_images_do_not_build_the_stand_in()
    {
        var dockerfile = File.ReadAllText(Path.Combine(Root, "deploy", "Dockerfile"));
        StandInWord.IsMatch(dockerfile).ShouldBeFalse();
        dockerfile.ShouldNotContain("tools/");
        dockerfile.ShouldNotContain("tests/");
    }

    [Fact]
    public void The_web_worker_and_migrator_do_not_load_the_stand_in_assembly()
    {
        foreach (var host in new[] { typeof(Lt2.Web.Components.App).Assembly, Assembly.Load("Lt2.Worker"), typeof(Lt2.Jobs.IdentityPurgeJob).Assembly, typeof(Lt2.Migrator.DatabaseRoles).Assembly })
        {
            ReferencedClosure(host).ShouldNotContain(StandInProject, host.GetName().Name);
        }
    }

    [Fact]
    public void No_production_configuration_names_the_stand_in()
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(Root, "src"), "appsettings*.json", SearchOption.AllDirectories)
            .Where(f => !IsBuildOutput(f) && StandInWord.IsMatch(File.ReadAllText(f)))
            .ToList();
        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void The_worker_imports_from_the_seimas_and_may_call_only_its_host()
    {
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "src", "Lt2.Worker", "appsettings.json")));
        settings.RootElement.GetProperty("Seimas").GetProperty("BaseAddress").GetString().ShouldBe("https://apps.lrs.lt/sip/");
        settings.RootElement.GetProperty("Outbound").GetProperty("AllowedHosts").EnumerateArray().Select(h => h.GetString())
            .ShouldBe(["apps.lrs.lt"]);
    }

    [Fact]
    public void Compose_runs_the_stand_in_only_in_the_local_profile_and_defaults_the_worker_to_the_seimas()
    {
        var services = ComposeServices(File.ReadAllLines(Path.Combine(Root, "deploy", "compose.yaml")));

        services.ShouldContainKey(StandInService);
        services[StandInService].ShouldContain(l => l.Trim() == "profiles: [local]");
        services[StandInService].ShouldContain(l => l.Contains("tools/Lt2.SeimasStandIn/Dockerfile", StringComparison.Ordinal));

        // No other service names the stand-in; the worker reaches it only through variables the local .env sets.
        foreach (var (name, lines) in services.Where(s => s.Key != StandInService))
        {
            lines.Where(l => !l.TrimStart().StartsWith('#')).ShouldNotContain(l => StandInWord.IsMatch(l), name);
        }

        services["worker"].ShouldContain(l => l.Trim() == "Seimas__BaseAddress: ${LT2_SEIMAS_BASE_ADDRESS:-https://apps.lrs.lt/sip/}");
        // Design 7.2: about one request a second to the Seimas unless a stack sets otherwise (only the stand-in's does).
        services["worker"].ShouldContain(l => l.Trim() == "Seimas__MinimumIntervalMilliseconds: ${LT2_SEIMAS_INTERVAL_MS:-1000}");
    }

    [Fact]
    public void Local_defaults_use_the_live_service_and_the_stand_in_is_opt_in()
    {
        var env = File.ReadAllLines(Path.Combine(Root, "deploy", ".env"));
        env.ShouldContain("COMPOSE_PROFILES=mailpit");
        env.Where(l => !l.TrimStart().StartsWith('#')).ShouldNotContain(l => StandInWord.IsMatch(l));

        var standIn = File.ReadAllLines(Path.Combine(Root, "deploy", "standin.env"));
        standIn.ShouldContain("COMPOSE_PROFILES=local");
        standIn.ShouldContain("LT2_SEIMAS_BASE_ADDRESS=http://seimas-standin:8080/sip/");
        standIn.ShouldContain("LT2_OUTBOUND_EXTRA_HOST=seimas-standin");
    }

    /// <summary>Service name → its lines, for the services block of a compose file (two-space indentation).</summary>
    private static Dictionary<string, List<string>> ComposeServices(string[] lines)
    {
        var services = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var inServices = false;
        List<string>? current = null;
        foreach (var line in lines)
        {
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]) && !line.StartsWith('#'))
            {
                inServices = line.StartsWith("services:", StringComparison.Ordinal);
                current = null;
                continue;
            }

            if (!inServices)
            {
                continue;
            }

            var header = Regex.Match(line, @"^  ([a-z0-9-]+):\s*$");
            if (header.Success)
            {
                current = services[header.Groups[1].Value] = [];
            }
            else
            {
                current?.Add(line);
            }
        }

        return services;
    }

    private static HashSet<string> ReferencedClosure(Assembly root)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<AssemblyName>([root.GetName()]);
        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            if (name.Name is null || !name.Name.StartsWith("Lt2", StringComparison.Ordinal) || !seen.Add(name.Name))
            {
                continue;
            }

            foreach (var reference in Assembly.Load(name).GetReferencedAssemblies())
            {
                queue.Enqueue(reference);
            }
        }

        return seen;
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LT2.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
