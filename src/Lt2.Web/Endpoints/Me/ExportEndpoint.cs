using System.Security.Claims;
using System.Text.Json;
using Lt2.Api.Contracts;
using Lt2.Identity;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Me;

/// <summary><c>GET /api/v1/me/export</c>: everything the person gave, as a JSON download (design 6.5). Only their own data.</summary>
internal static class ExportEndpoint
{
    private static readonly JsonSerializerOptions Indented = new(JsonSerializerOptions.Web) { WriteIndented = true };

    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet(ApiEndpoints.Relative(ApiRoutes.Me.Export), HandleAsync)
            .WithName("ExportMe")
            .WithTags("Me")
            .RequireAuthorization()
            .Produces(StatusCodes.Status200OK, contentType: "application/json");

    private static async Task<IResult> HandleAsync(ClaimsPrincipal user, IIdentityModule identity, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await identity.ExportAsync(user, cancellationToken);
        if (!result.IsSuccess)
        {
            return errors.ToResult(result.Error!);
        }

        var body = JsonSerializer.SerializeToUtf8Bytes(result.Value, Indented);
        return TypedResults.File(body, "application/json", "lietuva2-duomenys.json");
    }
}