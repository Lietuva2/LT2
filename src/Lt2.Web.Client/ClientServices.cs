using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Auth;
using Lt2.Api.Contracts.Me;
using Lt2.Web.Client.Api;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.Web.Client;

/// <summary>Marker for the client assembly, used by the server router.</summary>
public sealed class ClientAssembly;

public static class ClientServices
{
    public const string HttpClientName = "lt2-api";

    /// <summary>The browser implementations of the typed API clients: plain HTTP calls to <c>/api/v1</c>.</summary>
    public static IServiceCollection AddLt2HttpApiClients(this IServiceCollection services, Uri baseAddress)
    {
        services.AddTransient<AntiforgeryHandler>();
        services.AddSingleton<AntiforgeryTokenCache>();
        services.AddHttpClient(HttpClientName, client => client.BaseAddress = baseAddress)
            .AddHttpMessageHandler<AntiforgeryHandler>();
        services.AddScoped(sp => new ApiHttp(sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName)));
        services.AddScoped<IAuthApi, HttpAuthApi>();
        services.AddScoped<IMeApi, HttpMeApi>();
        services.AddScoped<Lt2.Api.Contracts.Appeals.IAppealsApi, HttpAppealsApi>();
        services.AddScoped<Lt2.Api.Contracts.Appeals.IModerationApi, HttpModerationApi>();
        services.AddScoped<Lt2.Api.Contracts.Appeals.IAdminApi, HttpAdminApi>();
        services.AddScoped<Lt2.Api.Contracts.Digests.IDigestsApi, HttpDigestsApi>();
        services.AddLt2Validators();
        services.AddLt2ClientComponents();
        return services;
    }

    /// <summary>Services the interactive components need wherever they render (in the browser or prerendered).</summary>
    public static IServiceCollection AddLt2ClientComponents(this IServiceCollection services)
    {
        services.AddScoped<Passkeys>();
        return services;
    }
}
