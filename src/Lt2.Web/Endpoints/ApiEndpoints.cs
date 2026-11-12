using FluentValidation;
using Lt2.Api.Contracts;
using Lt2.Web.Endpoints.Admin;
using Lt2.Web.Endpoints.Auth;
using Lt2.Web.Endpoints.Me;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints;

/// <summary>Maps every <c>/api/v1</c> endpoint. Each endpoint lives in its own file, grouped by module.</summary>
internal static class ApiEndpoints
{
    public static void MapLt2Api(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup(ApiRoutes.Prefix)
            .AddEndpointFilter<AntiforgeryAndOriginFilter>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        AntiforgeryTokenEndpoint.Map(api);
        RegisterEndpoint.Map(api);
        RequestCodeEndpoint.Map(api);
        SignInEndpoint.Map(api);
        SignOutEndpoint.Map(api);
        PasskeyCreationOptionsEndpoint.Map(api);
        AddPasskeyEndpoint.Map(api);
        PasskeyRequestOptionsEndpoint.Map(api);
        PasskeySignInEndpoint.Map(api);

        GetMeEndpoint.Map(api);
        GetSessionsEndpoint.Map(api);
        EndSessionEndpoint.Map(api);

        GetSettingsEndpoint.Map(api);
        UpdateSettingEndpoint.Map(api);
    }

    /// <summary>A route from <see cref="ApiRoutes"/> relative to the <c>/api/v1</c> group.</summary>
    public static string Relative(string route) => route[ApiRoutes.Prefix.Length..];

    /// <summary>Runs the shared FluentValidation validator for <typeparamref name="T"/> before the handler.</summary>
    public static RouteHandlerBuilder WithValidation<T>(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<ValidationFilter<T>>().ProducesValidationProblem();
}

internal sealed class ValidationFilter<T>(IValidator<T> validator, ErrorMapper errors) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var argument = context.Arguments.OfType<T>().FirstOrDefault();
        if (argument is null)
        {
            return errors.ValidationFailed(new Dictionary<string, string[]> { ["body"] = ["?"] });
        }

        var result = await validator.ValidateAsync(argument, context.HttpContext.RequestAborted);
        if (result.IsValid)
        {
            return await next(context);
        }

        return errors.ValidationFailed(result.Errors
            .GroupBy(e => JsonName(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));
    }

    private static string JsonName(string property) =>
        string.IsNullOrEmpty(property) ? property : char.ToLowerInvariant(property[0]) + property[1..];
}
