using System.Reflection;
using Lt2.Web.Client;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.UnitTests.Components;

/// <summary>
/// An island is created in the browser from the services the WebAssembly host registers (<see cref="ClientServices"/>), not
/// from the server's. A component that injects an API client the host does not register renders on the server (prerendered
/// with the server's implementation) but cannot be created in the browser: the console then shows "No root component exists
/// with SSR component ID N" when the page is left, and the island never works.
/// </summary>
public sealed class ClientServicesTests
{
    [Fact]
    public void Every_API_client_a_client_component_injects_is_registered_for_the_browser()
    {
        var services = new ServiceCollection();
        services.AddLt2HttpApiClients(new Uri("https://lt2.localhost/"));

        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var found = 0;
        foreach (var component in typeof(ClientAssembly).Assembly.GetTypes().Where(t => typeof(IComponent).IsAssignableFrom(t)))
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (var property in component.GetProperties(all).Where(p => p.GetCustomAttribute<InjectAttribute>() is not null))
            {
                if (property.PropertyType.Namespace?.StartsWith("Lt2.Api.Contracts", StringComparison.Ordinal) != true)
                {
                    continue;
                }

                found++;
                if (!services.Any(d => d.ServiceType == property.PropertyType))
                {
                    missing.Add($"{property.PropertyType.Name} (injected by {component.Name})");
                }
            }
        }

        found.ShouldBeGreaterThan(0, "the check finds the injected API clients (it is not empty)");
        missing.ShouldBeEmpty("API clients injected by client components but not registered in ClientServices.AddLt2HttpApiClients");
    }
}
