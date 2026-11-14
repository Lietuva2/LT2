using System.Security.Claims;
using Lt2.Api.Contracts.Organisations;
using Lt2.Appeals;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Organisations;

/// <summary>
/// Organisations (design 8.14.14, stage A): <c>GET /api/v1/organisations</c> (the public list, <c>q</c> searches the name),
/// <c>GET /organisations/{slug}</c> (the public profile), <c>POST /organisations</c> (a member registers one),
/// <c>PUT /organisations/{slug}</c> (a representative changes the profile), <c>GET /me/organisations</c> ("Mano
/// organizacijos") and <c>GET /organisations/{slug}/mine</c> (the caller's own management answer; representatives only
/// learn who the others are). The public answers are cacheable and name no representative.
/// </summary>
internal static class OrganisationEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet(ApiEndpoints.Relative(OrganisationRoutes.Organisations), ListAsync)
            .WithName("GetOrganisations").WithTags("Organisations").AllowAnonymous()
            .Produces<OrganisationListResponse>().Produces(StatusCodes.Status304NotModified);

        api.MapGet(ApiEndpoints.Relative(OrganisationRoutes.Organisation), ProfileAsync)
            .WithName("GetOrganisation").WithTags("Organisations").AllowAnonymous()
            .Produces<OrganisationProfileResponse>().Produces(StatusCodes.Status304NotModified).ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost(ApiEndpoints.Relative(OrganisationRoutes.Organisations), RegisterAsync)
            .WithName("RegisterOrganisation").WithTags("Organisations").RequireAuthorization(Policies.Member)
            .WithValidation<OrganisationRequest>().RequireRateLimiting(RateLimitPolicies.OpenAppeal)
            .Produces<OrganisationCreatedResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status409Conflict);

        api.MapPut(ApiEndpoints.Relative(OrganisationRoutes.Organisation), ChangeAsync)
            .WithName("ChangeOrganisation").WithTags("Organisations").RequireAuthorization(Policies.Member)
            .WithValidation<OrganisationRequest>().RequireRateLimiting(RateLimitPolicies.WriteReason)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        api.MapGet(ApiEndpoints.Relative(OrganisationRoutes.Mine), MineAsync)
            .WithName("GetMyOrganisations").WithTags("Organisations").RequireAuthorization(Policies.Member)
            .Produces<IReadOnlyList<MyOrganisationResponse>>();

        api.MapGet(ApiEndpoints.Relative(OrganisationRoutes.Management), ManagementAsync)
            .WithName("GetOrganisationManagement").WithTags("Organisations").RequireAuthorization(Policies.Member)
            .Produces<OrganisationManagementResponse>().ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListAsync(HttpContext http, string? q, IOrganisations organisations, ErrorMapper errors, CancellationToken cancellationToken) =>
        PublicJson.From(http, errors, Result<OrganisationListResponse>.Success(OrganisationMapping.Map(await organisations.ListAsync(q, cancellationToken))), maxAgeSeconds: 0);

    private static async Task<IResult> ProfileAsync(HttpContext http, string slug, IOrganisations organisations, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await organisations.ProfileAsync(slug, cancellationToken);
        return result.IsSuccess
            ? PublicJson.From(http, errors, Result<OrganisationProfileResponse>.Success(OrganisationMapping.Map(result.Value)), maxAgeSeconds: 0)
            : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> RegisterAsync(OrganisationRequest request, ClaimsPrincipal user, IOrganisations organisations, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await organisations.RegisterAsync(AppealMapping.UserId(user)!.Value, OrganisationMapping.ToModule(request), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Created(OrganisationRoutes.Of(result.Value.Slug), new OrganisationCreatedResponse(result.Value.Id, result.Value.Slug))
            : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> ChangeAsync(string slug, OrganisationRequest request, ClaimsPrincipal user, IOrganisations organisations, ErrorMapper errors, CancellationToken cancellationToken) =>
        Outcome(await organisations.ChangeAsync(slug, AppealMapping.UserId(user)!.Value, OrganisationMapping.ToModule(request), cancellationToken), errors);

    private static async Task<IResult> MineAsync(ClaimsPrincipal user, IOrganisations organisations, CancellationToken cancellationToken) =>
        TypedResults.Ok((await organisations.MineAsync(AppealMapping.UserId(user)!.Value, cancellationToken)).Select(OrganisationMapping.Map).ToList());

    private static async Task<IResult> ManagementAsync(string slug, ClaimsPrincipal user, IOrganisations organisations, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await organisations.ManagementAsync(slug, AppealMapping.UserId(user)!.Value, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(OrganisationMapping.Map(result.Value)) : errors.ToResult(result.Error!);
    }

    internal static IResult Outcome(Result<Unit> result, ErrorMapper errors) => result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
}

/// <summary>
/// A representative's actions on the organisation (design 8.14.14): confirmation by email domain
/// (<c>POST …/confirmation/code</c> sends a code to an address on the website's domain, <c>POST …/confirmation</c> enters it),
/// one-time links (<c>POST …/invites</c>; <c>GET /organisation-invites/{token}</c> and <c>…/accept</c> for the colleague),
/// removing another representative, leaving and closing.
/// </summary>
internal static class OrganisationRepresentativeEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapPost(ApiEndpoints.Relative(OrganisationRoutes.ConfirmationCode), SendCodeAsync)
            .WithName("SendOrganisationCode").WithTags("Organisations").RequireAuthorization(Policies.Member)
            .WithValidation<ConfirmationCodeRequest>().RequireRateLimiting(RateLimitPolicies.CodeRequest)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status409Conflict);

        api.MapPost(ApiEndpoints.Relative(OrganisationRoutes.Confirmation), ConfirmAsync)
            .WithName("ConfirmOrganisation").WithTags("Organisations").RequireAuthorization(Policies.Member)
            .WithValidation<ConfirmationRequest>().RequireRateLimiting(RateLimitPolicies.SignIn)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status409Conflict);

        api.MapPost(ApiEndpoints.Relative(OrganisationRoutes.Invites), InviteAsync)
            .WithName("CreateOrganisationInvite").WithTags("Organisations").RequireAuthorization(Policies.Member)
            .RequireRateLimiting(RateLimitPolicies.WriteReason)
            .Produces<InviteCreatedResponse>().ProducesProblem(StatusCodes.Status409Conflict);

        api.MapGet(ApiEndpoints.Relative(OrganisationRoutes.Invite), InviteViewAsync)
            .WithName("GetOrganisationInvite").WithTags("Organisations").RequireAuthorization(Policies.Member)
            .Produces<InviteResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost(ApiEndpoints.Relative(OrganisationRoutes.AcceptInvite), AcceptAsync)
            .WithName("AcceptOrganisationInvite").WithTags("Organisations").RequireAuthorization(Policies.Member)
            .Produces<OrganisationCreatedResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        api.MapDelete(ApiEndpoints.Relative(OrganisationRoutes.Representative), RemoveAsync)
            .WithName("RemoveOrganisationRepresentative").WithTags("Organisations").RequireAuthorization(Policies.Member)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost(ApiEndpoints.Relative(OrganisationRoutes.Leave), LeaveAsync)
            .WithName("LeaveOrganisation").WithTags("Organisations").RequireAuthorization(Policies.Member)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status409Conflict);

        api.MapPost(ApiEndpoints.Relative(OrganisationRoutes.Close), CloseAsync)
            .WithName("CloseOrganisation").WithTags("Organisations").RequireAuthorization(Policies.Member)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status409Conflict);

        // Design 8.14.14, stage B: the organisation's invitation code, its links and its one number, to representatives only.
        api.MapGet(ApiEndpoints.Relative(OrganisationRoutes.Invitation), InvitationAsync)
            .WithName("GetOrganisationInvitation").WithTags("Organisations").RequireAuthorization(Policies.Member)
            .Produces<OrganisationInvitationResponse>().ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost(ApiEndpoints.Relative(OrganisationRoutes.ReplaceInvitation), ReplaceInvitationAsync)
            .WithName("ReplaceOrganisationInvitation").WithTags("Organisations").RequireAuthorization(Policies.Member)
            .RequireRateLimiting(RateLimitPolicies.WriteReason)
            .Produces<OrganisationInvitationResponse>().ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> InvitationAsync(
        HttpContext http, string slug, ClaimsPrincipal user, IOrganisations organisations, Lt2.Identity.IOrganisationInvitations codes, IConfiguration configuration, ErrorMapper errors, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await OrganisationInvitationMapping.GetAsync(organisations, codes, configuration, slug, AppealMapping.UserId(user)!.Value, replace: false, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> ReplaceInvitationAsync(
        HttpContext http, string slug, ClaimsPrincipal user, IOrganisations organisations, Lt2.Identity.IOrganisationInvitations codes, IConfiguration configuration, ErrorMapper errors, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await OrganisationInvitationMapping.GetAsync(organisations, codes, configuration, slug, AppealMapping.UserId(user)!.Value, replace: true, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> SendCodeAsync(string slug, ConfirmationCodeRequest request, ClaimsPrincipal user, IOrganisations organisations, ErrorMapper errors, CancellationToken cancellationToken) =>
        OrganisationEndpoints.Outcome(await organisations.SendCodeAsync(slug, AppealMapping.UserId(user)!.Value, request.Address, cancellationToken), errors);

    private static async Task<IResult> ConfirmAsync(string slug, ConfirmationRequest request, ClaimsPrincipal user, IOrganisations organisations, ErrorMapper errors, CancellationToken cancellationToken) =>
        OrganisationEndpoints.Outcome(await organisations.ConfirmAsync(slug, AppealMapping.UserId(user)!.Value, request.Code, cancellationToken), errors);

    private static async Task<IResult> InviteAsync(string slug, ClaimsPrincipal user, IOrganisations organisations, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await organisations.InviteAsync(slug, AppealMapping.UserId(user)!.Value, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(OrganisationMapping.Map(result.Value)) : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> InviteViewAsync(string token, ClaimsPrincipal user, IOrganisations organisations, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await organisations.InviteViewAsync(token, AppealMapping.UserId(user)!.Value, cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(new InviteResponse(result.Value.Slug, result.Value.Name, result.Value.Usable, result.Value.AlreadyRepresentative))
            : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> AcceptAsync(string token, ClaimsPrincipal user, IOrganisations organisations, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await organisations.AcceptInviteAsync(token, AppealMapping.UserId(user)!.Value, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(new OrganisationCreatedResponse(Guid.Empty, result.Value)) : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> RemoveAsync(string slug, Guid id, ClaimsPrincipal user, IOrganisations organisations, ErrorMapper errors, CancellationToken cancellationToken) =>
        OrganisationEndpoints.Outcome(await organisations.RemoveRepresentativeAsync(slug, id, AppealMapping.UserId(user)!.Value, cancellationToken), errors);

    private static async Task<IResult> LeaveAsync(string slug, ClaimsPrincipal user, IOrganisations organisations, ErrorMapper errors, CancellationToken cancellationToken) =>
        OrganisationEndpoints.Outcome(await organisations.LeaveAsync(slug, AppealMapping.UserId(user)!.Value, cancellationToken), errors);

    private static async Task<IResult> CloseAsync(string slug, ClaimsPrincipal user, IOrganisations organisations, ErrorMapper errors, CancellationToken cancellationToken) =>
        OrganisationEndpoints.Outcome(await organisations.CloseAsync(slug, AppealMapping.UserId(user)!.Value, cancellationToken), errors);
}

/// <summary>
/// Statements on a bill (design 8.14.3, 8.14.14): <c>GET /api/v1/bills/{number}/organisations</c> is public and the same
/// for everyone ("Organizacijų pozicijos"); <c>GET …/organisations/mine</c> is the caller's own answer for a representative's
/// controls, never cached; <c>PUT</c> and <c>DELETE …/organisations/{slug}/statement</c> publish, change and withdraw.
/// </summary>
internal static class StatementEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet(ApiEndpoints.Relative(OrganisationRoutes.OnBill), OnBillAsync)
            .WithName("GetBillStatements").WithTags("Organisations").AllowAnonymous()
            .Produces<BillStatementsResponse>().Produces(StatusCodes.Status304NotModified).ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGet(ApiEndpoints.Relative(OrganisationRoutes.MineOnBill), MineAsync)
            .WithName("GetMyBillStatements").WithTags("Organisations").RequireAuthorization(Policies.Member)
            .Produces<IReadOnlyList<MyBillOrganisationResponse>>().ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPut(ApiEndpoints.Relative(OrganisationRoutes.Statement), SaveAsync)
            .WithName("SaveStatement").WithTags("Organisations").RequireAuthorization(Policies.Member)
            .WithValidation<OrganisationStatementRequest>().RequireRateLimiting(RateLimitPolicies.WriteReason)
            .Produces<StatementSavedResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        api.MapDelete(ApiEndpoints.Relative(OrganisationRoutes.Statement), WithdrawAsync)
            .WithName("WithdrawStatement").WithTags("Organisations").RequireAuthorization(Policies.Member)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> OnBillAsync(HttpContext http, string number, IOrganisations organisations, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await organisations.OnBillAsync(number, cancellationToken);
        return result.IsSuccess
            ? PublicJson.From(http, errors, Result<BillStatementsResponse>.Success(OrganisationMapping.Map(result.Value)), maxAgeSeconds: 0)
            : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> MineAsync(string number, ClaimsPrincipal user, IOrganisations organisations, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await organisations.MineOnBillAsync(number, AppealMapping.UserId(user)!.Value, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value.Select(OrganisationMapping.Map).ToList()) : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> SaveAsync(
        string number, string slug, OrganisationStatementRequest request, ClaimsPrincipal user, IOrganisations organisations, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await organisations.SaveStatementAsync(number, slug, AppealMapping.UserId(user)!.Value, OrganisationMapping.ToModule(request), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(new StatementSavedResponse(result.Value.Id, result.Value.Status)) : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> WithdrawAsync(string number, string slug, ClaimsPrincipal user, IOrganisations organisations, ErrorMapper errors, CancellationToken cancellationToken) =>
        OrganisationEndpoints.Outcome(await organisations.WithdrawStatementAsync(number, slug, AppealMapping.UserId(user)!.Value, cancellationToken), errors);
}
