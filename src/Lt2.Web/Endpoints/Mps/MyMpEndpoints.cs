using System.Security.Claims;
using Lt2.Api.Contracts.Mps;
using Lt2.Identity;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Mps;

/// <summary>
/// The account's own link to an MP (design 8.5.3, 8.5.4, 8.8): <c>DELETE /api/v1/me/mp</c> (give the profile up, or leave
/// the office), <c>GET /api/v1/me/mp/office</c> (the MP's account only), <c>POST …/office/invitations</c> (a one-time link,
/// shown once; emailed by the site to an address given, owner 2026-11-17), <c>DELETE …/office/{id}</c> and <c>POST …/office/acceptance</c> (the invited member, in a passkey session or
/// bringing a passkey in the same request, owner 2026-11-17).
/// Speaking for the MP needs a passkey session, read from the database on every request.
/// </summary>
internal static class MyMpEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapDelete(ApiEndpoints.Relative(MpRoutes.Mine), EndAsync)
            .WithName("EndMyMpLink").WithTags("Mps").RequireAuthorization(Policies.Member)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGet(ApiEndpoints.Relative(MpRoutes.Office), OfficeAsync)
            .WithName("GetMyMpOffice").WithTags("Mps").RequireAuthorization(Policies.Member)
            .Produces<MpOfficeResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost(ApiEndpoints.Relative(MpRoutes.OfficeInvitations), InviteAsync)
            .WithName("InviteMpOfficeMember").WithTags("Mps").RequireAuthorization(Policies.Member)
            .WithValidation<MpOfficeInvitationRequest>()
            .Produces<MpOfficeInvitationResponse>().ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status429TooManyRequests);

        api.MapDelete(ApiEndpoints.Relative(MpRoutes.OfficeMember), RemoveAsync)
            .WithName("RemoveMpOfficeMember").WithTags("Mps").RequireAuthorization(Policies.Member)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost(ApiEndpoints.Relative(MpRoutes.OfficeAcceptance), AcceptAsync)
            .WithName("AcceptMpOfficeInvitation").WithTags("Mps").RequireAuthorization(Policies.Member)
            .WithValidation<MpOfficeAcceptanceRequest>()
            .RequireRateLimiting(RateLimitPolicies.OneTimeCode)
            .Produces<MpLinkResponse>().ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> EndAsync(
        ClaimsPrincipal user, HttpContext context, IpHasher ipHasher, IPoliticians politicians, ErrorMapper errors, CancellationToken cancellationToken) =>
        Done(await politicians.EndOwnAsync(user, ipHasher.Hash(context), cancellationToken), errors);

    private static async Task<IResult> OfficeAsync(ClaimsPrincipal user, IPoliticians politicians, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await politicians.OfficeAsync(user, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(MpMapping.Map(result.Value)) : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> InviteAsync(
        MpOfficeInvitationRequest request, ClaimsPrincipal user, HttpContext context, IpHasher ipHasher, IPoliticians politicians, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await politicians.InviteOfficeMemberAsync(user, request.Email, ipHasher.Hash(context), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(MpMapping.Map(result.Value)) : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> RemoveAsync(
        Guid id, ClaimsPrincipal user, HttpContext context, IpHasher ipHasher, IPoliticians politicians, ErrorMapper errors, CancellationToken cancellationToken) =>
        Done(await politicians.RemoveOfficeMemberAsync(user, id, ipHasher.Hash(context), cancellationToken), errors);

    private static async Task<IResult> AcceptAsync(
        MpOfficeAcceptanceRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        IpHasher ipHasher,
        IPoliticians politicians,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var result = await politicians.AcceptOfficeInvitationAsync(user, request.Token, request.NewPasskey, request.Passkey, ipHasher.Hash(context), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(MpMapping.Map(result.Value)) : errors.ToResult(result.Error!);
    }

    private static IResult Done(Result<Unit> result, ErrorMapper errors) =>
        result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
}
