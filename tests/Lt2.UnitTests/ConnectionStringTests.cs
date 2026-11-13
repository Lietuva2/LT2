using Lt2.SharedKernel.Persistence;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Lt2.UnitTests;

/// <summary>Design 9.5: every pool has a stated size, and a host that forgets one gets a small pool, never Npgsql's 100.</summary>
public sealed class ConnectionStringTests
{
    private static NpgsqlConnectionStringBuilder Built(params (string Key, string Value)[] settings)
    {
        var values = new Dictionary<string, string?> { ["ConnectionStrings:Lt2"] = "Host=db;Database=lt2;Username=lt2_app" };
        foreach (var (key, value) in settings)
        {
            values[key] = value;
        }

        return new NpgsqlConnectionStringBuilder(
            ConnectionStrings.Build(new ConfigurationBuilder().AddInMemoryCollection(values).Build()));
    }

    [Fact]
    public void The_pool_size_and_name_come_from_configuration()
    {
        var built = Built(("Database:MaxPoolSize", "25"), ("Database:ApplicationName", "lt2-web"), ("Database:Password", "x"));
        built.MaxPoolSize.ShouldBe(25);
        built.ApplicationName.ShouldBe("lt2-web");
        built.Password.ShouldBe("x");
    }

    [Fact]
    public void A_host_that_sets_no_pool_size_gets_a_small_pool_not_npgsqls_hundred()
    {
        Built().MaxPoolSize.ShouldBe(ConnectionStrings.DefaultMaxPoolSize);
        ConnectionStrings.DefaultMaxPoolSize.ShouldBeLessThan(100);
    }
}
