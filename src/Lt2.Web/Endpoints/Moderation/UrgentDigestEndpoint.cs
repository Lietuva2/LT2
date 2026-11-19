using System.Security.Claims;
using Lt2.Api.Contracts.Digests;
using Lt2.Notifications;
using Lt2.Web.Endpoints.Admin;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Moderation;

/// <summary>
/// <c>POST /api/v1/moderation/records/{id}/urgent</c>: a moderator approves one urgent summary for the record's bill
/// (design 8.4): to every MP, both sides, may exceed the weekly ceiling by one. A dry run while sending is off. Like the other
/// sensitive actions it needs the staff passkey confirmed in the last ten minutes (design 6.2, <see cref="FreshConfirmation"/>;
/// otherwise 403 <c>passkey.confirmation-required</c> and nothing is made); the in-process client asks the same. Audited.
/// </summary>
internal static class UrgentDigestEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(DigestRoutes.Urgent), HandleAsync)
            .WithName("UrgentDigest")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .WithValidation<UrgentDigestRequest>()
            .Produces<DigestRunResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> HandleAsync(
        Guid id,
        UrgentDigestRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        Lt2.Identity.IIdentityModule identity,
        IDigests digests,
        IpHasher ipHasher,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        if (await FreshConfirmation.RequireAsync(identity, user, errors, cancellationToken) is { } refused)
        {
            return refused;
        }

        var moderator = await AppealMapping.ModeratorAsync(identity, user, ipHasher.Hash(context), cancellationToken);
        var result = await digests.UrgentAsync(id, request.Statement, moderator, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created(DigestRoutes.DigestOn(result.Value.RunDate), DigestMapping.Map(result.Value))
            : errors.ToResult(result.Error!);
    }
}
