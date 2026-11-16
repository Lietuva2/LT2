using System.Security.Claims;
using Lt2.Api.Contracts.Digests;
using Lt2.Identity;
using Lt2.Notifications;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Admin;

/// <summary>
/// Administrators and MP mail (design 8.19.5, 8.19.6, 8.19.7): <c>GET|PUT /api/v1/admin/introduction</c> (the letter),
/// <c>POST …/test</c> (a test copy to the administrator's own address), <c>POST …/send</c> (with a fresh passkey confirmation),
/// and <c>GET|POST|DELETE /api/v1/admin/suppression</c>. Each change is audited without an address.
/// </summary>
internal static class MpSendingAdminEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet(ApiEndpoints.Relative(MpSendingAdminRoutes.Introduction), GetAsync)
            .WithName("GetIntroductionAdmin").WithTags("Admin").RequireAuthorization(Policies.Administrator)
            .Produces<IntroductionResponse>();

        api.MapPut(ApiEndpoints.Relative(MpSendingAdminRoutes.Introduction), SaveAsync)
            .WithName("SaveIntroduction").WithTags("Admin").RequireAuthorization(Policies.Administrator)
            .WithValidation<IntroductionRequest>()
            .Produces<IntroductionVersionResponse>();

        api.MapPost(ApiEndpoints.Relative(MpSendingAdminRoutes.IntroductionTest), TestAsync)
            .WithName("TestIntroduction").WithTags("Admin").RequireAuthorization(Policies.Administrator)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status409Conflict);

        api.MapPost(ApiEndpoints.Relative(MpSendingAdminRoutes.IntroductionSend), SendAsync)
            .WithName("SendIntroduction").WithTags("Admin").RequireAuthorization(Policies.Administrator)
            .Produces<IntroductionSentResponse>().ProducesProblem(StatusCodes.Status409Conflict);

        api.MapGet(ApiEndpoints.Relative(MpSendingAdminRoutes.Suppression), SuppressionAsync)
            .WithName("GetSuppression").WithTags("Admin").RequireAuthorization(Policies.Administrator)
            .Produces<SuppressionResponse>();

        api.MapPost(ApiEndpoints.Relative(MpSendingAdminRoutes.Suppression), SuppressAsync)
            .WithName("Suppress").WithTags("Admin").RequireAuthorization(Policies.Administrator)
            .WithValidation<SuppressRequest>()
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status409Conflict);

        api.MapDelete(ApiEndpoints.Relative(MpSendingAdminRoutes.Suppression), UnsuppressAsync)
            .WithName("Unsuppress").WithTags("Admin").RequireAuthorization(Policies.Administrator)
            .WithValidation<UnsuppressRequest>()
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> GetAsync(IMpMailAdministration admin, CancellationToken ct) =>
        TypedResults.Ok(MpSendingMapping.Map(await admin.IntroductionAsync(ct)));

    private static async Task<IResult> SaveAsync(
        IntroductionRequest request, ClaimsPrincipal user, HttpContext context, IpHasher ipHasher, IMpMailAdministration admin, ErrorMapper errors, CancellationToken ct)
    {
        var result = await admin.SaveIntroductionAsync(MpSendingMapping.Draft(request), Actor(user, context, ipHasher), ct);
        return result.IsSuccess ? TypedResults.Ok(DigestMapping.Map(result.Value)) : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> TestAsync(
        ClaimsPrincipal user, HttpContext context, IpHasher ipHasher, MpMailHost host, ErrorMapper errors, CancellationToken ct) =>
        Done(await host.TestAsync(user, Actor(user, context, ipHasher), ct), errors);

    private static async Task<IResult> SendAsync(
        ClaimsPrincipal user, HttpContext context, IpHasher ipHasher, IIdentityModule identity, IMpMailAdministration admin, ErrorMapper errors, CancellationToken ct)
    {
        // Design 8.19.5: one press with the number of MPs in the confirmation and a fresh passkey confirmation.
        if (await FreshConfirmation.RequireAsync(identity, user, errors, ct) is { } refused)
        {
            return refused;
        }

        var result = await admin.SendIntroductionAsync(Actor(user, context, ipHasher), ct);
        return result.IsSuccess ? TypedResults.Ok(new IntroductionSentResponse(result.Value)) : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> SuppressionAsync(IMpMailAdministration admin, CancellationToken ct) =>
        TypedResults.Ok(MpSendingMapping.Map(await admin.SuppressionAsync(ct)));

    private static async Task<IResult> SuppressAsync(
        SuppressRequest request, ClaimsPrincipal user, HttpContext context, IpHasher ipHasher, IMpMailAdministration admin, ErrorMapper errors, CancellationToken ct) =>
        Done(await admin.SuppressAsync(request.Address, request.Statement, Actor(user, context, ipHasher), ct), errors);

    private static async Task<IResult> UnsuppressAsync(
        [Microsoft.AspNetCore.Mvc.FromBody] UnsuppressRequest request, ClaimsPrincipal user, HttpContext context, IpHasher ipHasher, IMpMailAdministration admin, ErrorMapper errors, CancellationToken ct) =>
        Done(await admin.UnsuppressAsync(request.EntryId, request.Address, request.Statement, Actor(user, context, ipHasher), ct), errors);

    private static StaffActor Actor(ClaimsPrincipal user, HttpContext context, IpHasher ipHasher) =>
        new(user.UserId()!.Value, Roles.Administrator, ipHasher.Hash(context));

    private static IResult Done(Result<Unit> result, ErrorMapper errors) =>
        result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
}
