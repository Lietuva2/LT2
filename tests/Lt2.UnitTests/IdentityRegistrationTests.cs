using Lt2.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Lt2.UnitTests;

/// <summary>
/// Every host that uses the identity database tells Identity to build the version 3 schema (with passkeys). Without it the
/// model cannot be built in a fresh process: the migrator's role grant and the worker's daily cleanup failed in their
/// containers, while tests passed on a model cached from the web host.
/// </summary>
public sealed class IdentityRegistrationTests
{
    public static TheoryData<string> Hosts() => new("web", "worker", "migrator");

    [Theory]
    [MemberData(nameof(Hosts))]
    public void Every_host_builds_the_identity_schema_with_passkeys(string host)
    {
        var services = new ServiceCollection();
        services.AddSingleton(NpgsqlDataSource.Create("Host=unused"));
        _ = host switch
        {
            "web" => services.AddIdentityModule(new ConfigurationBuilder().Build()),
            "worker" => services.AddIdentityMaintenance(),
            _ => services.AddIdentityAdministration(),
        };
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<IdentityOptions>>().Value.Stores.SchemaVersion
            .ShouldBe(IdentitySchemaVersions.Version3);
    }
}
