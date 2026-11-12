using FluentValidation;
using Lt2.Api.Contracts.Admin;
using Lt2.Api.Contracts.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.Api.Contracts;

public static class ContractsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared validators explicitly (no assembly scanning, so trimming in the browser keeps them).
    /// The same validators run in the browser and on the server (design 3).
    /// </summary>
    public static IServiceCollection AddLt2Validators(this IServiceCollection services)
    {
        services.AddScoped<IValidator<RegisterRequest>, RegisterRequestValidator>();
        services.AddScoped<IValidator<RequestCodeRequest>, RequestCodeRequestValidator>();
        services.AddScoped<IValidator<SignInRequest>, SignInRequestValidator>();
        services.AddScoped<IValidator<PasskeyCredentialRequest>, PasskeyCredentialRequestValidator>();
        services.AddScoped<IValidator<UpdateSettingRequest>, UpdateSettingRequestValidator>();
        return services;
    }
}
