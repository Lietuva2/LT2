using System.Reflection;
using System.Text.RegularExpressions;
using Lt2.Appeals;
using Lt2.Audit;
using Lt2.Identity;
using Lt2.Notifications;
using Lt2.Parliament;
using Microsoft.EntityFrameworkCore;
using NetArchTest.Rules;

namespace Lt2.ArchitectureTests;

/// <summary>Design 2 and 4: modules do not reach into each other; each keeps to its own schema; internals stay internal.</summary>
public sealed class ModuleBoundaryTests
{
    public sealed record Module(string Name, Assembly Assembly, string[] Schemas)
    {
        public override string ToString() => Name;
    }

    private static readonly Module[] Modules =
    [
        new("Lt2.Identity", typeof(IdentityModule).Assembly, ["identity"]),
        new("Lt2.Parliament", typeof(ParliamentModule).Assembly, ["parliament"]),
        new("Lt2.Appeals", typeof(AppealsModule).Assembly, ["appeals"]),
        new("Lt2.Notifications", typeof(NotificationsModule).Assembly, ["notify"]),
        new("Lt2.Audit", typeof(AuditModule).Assembly, ["audit", "config"]),
    ];

    private static readonly string[] AllSchemas = Modules.SelectMany(m => m.Schemas).Concat(["hangfire"]).ToArray();

    public static TheoryData<Module> EachModule() => new(Modules);

    [Theory]
    [MemberData(nameof(EachModule))]
    public void A_module_does_not_depend_on_another_module(Module module)
    {
        var others = Modules.Where(m => m != module).Select(m => m.Name).ToArray();
        var result = Types.InAssembly(module.Assembly).ShouldNot().HaveDependencyOnAny(others).GetResult();
        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Theory]
    [MemberData(nameof(EachModule))]
    public void A_module_does_not_depend_on_the_hosts_or_the_api_contracts(Module module)
    {
        var result = Types.InAssembly(module.Assembly)
            .ShouldNot().HaveDependencyOnAny("Lt2.Web", "Lt2.Web.Client", "Lt2.Worker", "Lt2.Migrator", "Lt2.Api.Contracts")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Theory]
    [MemberData(nameof(EachModule))]
    public void Only_the_module_interface_its_types_and_its_registration_are_public(Module module)
    {
        var result = Types.InAssembly(module.Assembly)
            .That().ArePublic()
            .And().DoNotResideInNamespace(module.Name + ".Infrastructure.Migrations")
            .Should().ResideInNamespaceMatching("^" + Regex.Escape(module.Name) + "$")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Theory]
    [MemberData(nameof(EachModule))]
    public void A_module_maps_tables_only_in_its_own_schemas(Module module)
    {
        foreach (var contextType in module.Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(DbContext)) && !t.IsAbstract))
        {
            using var context = CreateContext(contextType);
            var schemas = context.Model.GetEntityTypes()
                .Where(e => e.GetTableName() is not null)
                .Select(e => e.GetSchema() ?? context.Model.GetDefaultSchema())
                .Distinct()
                .ToList();
            schemas.ShouldAllBe(s => module.Schemas.Contains(s), $"{contextType.Name} maps tables in {string.Join(", ", schemas)}");
        }
    }

    [Theory]
    [MemberData(nameof(EachModule))]
    public void Raw_sql_in_a_module_names_only_its_own_schemas(Module module)
    {
        var folder = Path.Combine(RepositoryRoot(), "src", "Modules", module.Name);
        var foreign = AllSchemas.Except(module.Schemas).ToArray();
        var pattern = new Regex(@"\b(" + string.Join("|", foreign) + @")\.[a-z_""]", RegexOptions.CultureInvariant);
        var offences = Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Where(l => !l.Text.TrimStart().StartsWith("//", StringComparison.Ordinal) && pattern.IsMatch(l.Text))
            .Select(l => $"{Path.GetFileName(l.File)}:{l.Line}: {l.Text.Trim()}")
            .ToList();
        offences.ShouldBeEmpty();
    }

    [Fact]
    public void Endpoints_and_pages_do_not_use_a_database_directly()
    {
        var result = Types.InAssembly(typeof(Lt2.Web.Components.PageText).Assembly)
            .That().ResideInNamespaceStartingWith("Lt2.Web.Endpoints")
            .Or().ResideInNamespaceStartingWith("Lt2.Web.Components")
            .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void The_browser_client_depends_only_on_the_api_contracts()
    {
        var result = Types.InAssembly(typeof(Lt2.Web.Client.ClientAssembly).Assembly)
            .ShouldNot().HaveDependencyOnAny(Modules.Select(m => m.Name).Concat(["Lt2.SharedKernel", "Lt2.Web.Services"]).ToArray())
            .GetResult();
        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    private static DbContext CreateContext(Type contextType)
    {
        if (contextType.GetMethod("StandaloneOptions", BindingFlags.NonPublic | BindingFlags.Static) is { } standalone)
        {
            return (DbContext)Activator.CreateInstance(contextType, standalone.Invoke(null, ["Host=unused"]))!;
        }

        var builderType = typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType);
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(builderType)!;
        builder.UseNpgsql("Host=unused").UseSnakeCaseNamingConvention();
        return (DbContext)Activator.CreateInstance(contextType, builder.Options)!;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LT2.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string Describe(NetArchTest.Rules.TestResult result) =>
        "Offending types: " + string.Join(", ", result.FailingTypeNames ?? []);
}
