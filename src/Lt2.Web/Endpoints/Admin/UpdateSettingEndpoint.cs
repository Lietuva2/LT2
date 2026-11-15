using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Audit;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Admin;

/// <summary>
/// <c>PUT /api/v1/admin/settings/{key}</c>: changes one setting; the change is written to the audit log. Needs a fresh
/// passkey confirmation (design 6.9).
/// </summary>
internal static class UpdateSettingEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPut(ApiEndpoints.Relative(ApiRoutes.Admin.Setting), HandleAsync)
            .WithName("UpdateSetting")
            .WithTags("Admin")
            .RequireAuthorization(Policies.Administrator)
            .WithValidation<UpdateSettingRequest>()
            .Produces<SettingResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> HandleAsync(
        string key,
        UpdateSettingRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        ISettingsAdministration settings,
        Lt2.Identity.IIdentityModule identity,
        IpHasher ipHasher,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        // Design 6.9: saving a setting asks for the passkey again, as changing roles does.
        if (await FreshConfirmation.RequireAsync(identity, user, errors, cancellationToken) is { } refused)
        {
            return refused;
        }

        var actor = user.UserId()!.Value;
        var result = await settings.UpdateAsync(key, request.Value, actor, Roles.Administrator, ipHasher.Hash(context), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(SettingMapping.ToResponse(result.Value)) : errors.ToResult(result.Error!);
    }
}
