using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Moderation;

/// <summary>
/// <c>POST /api/v1/moderation/reasons/{id}/restrict-author</c>: a moderator or an administrator restricts the argument's
/// author from writing until a day or until lifted (design 8.3), in a staff-passkey session; the statement is required and emailed with the contact address; audited.
/// </summary>
internal static class RestrictAuthorEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapPost(ApiEndpoints.Relative(AppealRoutes.RestrictReasonAuthor), (Guid id, RestrictionRequest request, ClaimsPrincipal user, HttpContext context, Lt2.Identity.IIdentityModule identity,
                IAppealsModeration moderation, IpHasher ipHasher, ErrorMapper errors, IConfiguration configuration, CancellationToken cancellationToken) =>
                HandleAsync((m, contact) => moderation.RestrictAuthorOfReasonAsync(id, request.Until, request.Statement, m, contact, cancellationToken), user, context, identity, ipHasher, errors, configuration, cancellationToken))
            .WithName("RestrictReasonAuthor")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .WithValidation<RestrictionRequest>()
            .Produces<RestrictionCreatedResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> HandleAsync(
        Func<ModeratorActor, string?, Task<Lt2.SharedKernel.Result<Guid>>> restrict,
        ClaimsPrincipal user,
        HttpContext context,
        Lt2.Identity.IIdentityModule identity,
        IpHasher ipHasher,
        ErrorMapper errors,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var moderator = await AppealMapping.ModeratorAsync(identity, user, ipHasher.Hash(context), cancellationToken);
        var result = await restrict(moderator, AppealMapping.Contact(configuration));
        return result.IsSuccess
            ? TypedResults.Created((string?)null, new RestrictionCreatedResponse(result.Value))
            : errors.ToResult(result.Error!);
    }
}
