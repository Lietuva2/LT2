using System.Security.Claims;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Organisations;
using Lt2.Appeals;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Organisations;

/// <summary>
/// Moderation of organisations and statements (design 8.14.9, 8.14.14), moderators in a staff-passkey session only:
/// <c>GET /api/v1/moderation/organisations</c> (the queue's part), <c>…/hidden</c> (blocked organisations and hidden
/// statements), <c>GET /moderation/bills/{number}/statements</c> (the "Moderavimas" panel on the bill's page), and the actions:
/// keep, block, unblock, confirm, unconfirm an organisation; publish, hide, restore a statement; the writer's screen and
/// restricting the writer, as for an argument. Each action is audited and emailed to the organisation's representatives.
/// </summary>
internal static class OrganisationModerationEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet(ApiEndpoints.Relative(OrganisationRoutes.ModerationQueue), async (IOrganisationModeration moderation, CancellationToken ct) =>
                TypedResults.Ok(OrganisationMapping.Map(await moderation.QueueAsync(ct))))
            .WithName("GetOrganisationQueue").WithTags("Moderation").RequireAuthorization(Policies.Moderator).Produces<OrganisationModerationResponse>();

        api.MapGet(ApiEndpoints.Relative(OrganisationRoutes.ModerationHidden), async (IOrganisationModeration moderation, CancellationToken ct) =>
                TypedResults.Ok(OrganisationMapping.Map(await moderation.HiddenAsync(ct))))
            .WithName("GetHiddenOrganisations").WithTags("Moderation").RequireAuthorization(Policies.Moderator).Produces<HiddenOrganisationsResponse>();

        api.MapGet(ApiEndpoints.Relative(OrganisationRoutes.ModerationBill), async (string number, IOrganisationModeration moderation, ErrorMapper errors, CancellationToken ct) =>
            {
                var result = await moderation.OnBillAsync(number, ct);
                return result.IsSuccess ? TypedResults.Ok(result.Value.Select(OrganisationMapping.Map).ToList()) : errors.ToResult(result.Error!);
            })
            .WithName("GetModerationBillStatements").WithTags("Moderation").RequireAuthorization(Policies.Moderator)
            .Produces<IReadOnlyList<QueueStatementResponse>>().ProducesProblem(StatusCodes.Status404NotFound);

        Act<ModerationRequest>(api, OrganisationRoutes.Keep, "KeepOrganisation", (m, id, r, a, c, ct) => m.KeepAsync(id, r.Statement, a, c, ct));
        Act<StatementRequest>(api, OrganisationRoutes.Block, "BlockOrganisation", (m, id, r, a, c, ct) => m.BlockAsync(id, r.Statement, a, c, ct));
        Act<StatementRequest>(api, OrganisationRoutes.Unblock, "UnblockOrganisation", (m, id, r, a, c, ct) => m.UnblockAsync(id, r.Statement, a, c, ct));
        Act<StatementRequest>(api, OrganisationRoutes.Confirm, "ConfirmOrganisationByHand", (m, id, r, a, c, ct) => m.ConfirmAsync(id, r.Statement, a, c, ct));
        Act<StatementRequest>(api, OrganisationRoutes.Unconfirm, "WithdrawOrganisationConfirmation", (m, id, r, a, c, ct) => m.WithdrawConfirmationAsync(id, r.Statement, a, c, ct));
        Act<ModerationRequest>(api, OrganisationRoutes.PublishStatement, "PublishStatement", (m, id, r, a, c, ct) => m.PublishStatementAsync(id, r.Statement, a, c, ct));
        Act<StatementRequest>(api, OrganisationRoutes.HideStatement, "HideStatement", (m, id, r, a, c, ct) => m.HideStatementAsync(id, r.Statement, a, c, ct));
        Act<StatementRequest>(api, OrganisationRoutes.RestoreStatement, "RestoreStatement", (m, id, r, a, c, ct) => m.RestoreStatementAsync(id, r.Statement, a, c, ct));

        api.MapGet(ApiEndpoints.Relative(OrganisationRoutes.StatementWriter), async (Guid id, IOrganisationModeration moderation, ErrorMapper errors, CancellationToken ct) =>
            {
                var result = await moderation.WriterOfStatementAsync(id, ct);
                return result.IsSuccess ? TypedResults.Ok(AppealMapping.Map(result.Value)) : errors.ToResult(result.Error!);
            })
            .WithName("GetStatementWriter").WithTags("Moderation").RequireAuthorization(Policies.Moderator)
            .Produces<AuthorViewResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPost(ApiEndpoints.Relative(OrganisationRoutes.RestrictStatementWriter), RestrictAsync)
            .WithName("RestrictStatementWriter").WithTags("Moderation").RequireAuthorization(Policies.Moderator)
            .WithValidation<RestrictionRequest>()
            .Produces<RestrictionCreatedResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
    }

    private delegate Task<Result<Unit>> Action<in T>(IOrganisationModeration moderation, Guid id, T request, ModeratorActor moderator, string? contact, CancellationToken ct);

    private static void Act<T>(IEndpointRouteBuilder api, string route, string name, Action<T> action) =>
        api.MapPost(ApiEndpoints.Relative(route), async (
                Guid id, T request, ClaimsPrincipal user, HttpContext context, Lt2.Identity.IIdentityModule identity, IOrganisationModeration moderation,
                IpHasher ipHasher, ErrorMapper errors, IConfiguration configuration, CancellationToken ct) =>
            {
                var moderator = await AppealMapping.ModeratorAsync(identity, user, ipHasher.Hash(context), ct);
                return OrganisationEndpoints.Outcome(await action(moderation, id, request, moderator, AppealMapping.Contact(configuration), ct), errors);
            })
            .WithName(name).WithTags("Moderation").RequireAuthorization(Policies.Moderator)
            .WithValidation<T>()
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> RestrictAsync(
        Guid id, RestrictionRequest request, ClaimsPrincipal user, HttpContext context, Lt2.Identity.IIdentityModule identity, IOrganisationModeration moderation,
        IpHasher ipHasher, ErrorMapper errors, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var moderator = await AppealMapping.ModeratorAsync(identity, user, ipHasher.Hash(context), cancellationToken);
        var result = await moderation.RestrictWriterOfStatementAsync(id, request.Until, request.Statement, moderator, AppealMapping.Contact(configuration), cancellationToken);
        return result.IsSuccess ? TypedResults.Created(Lt2.Api.Contracts.AppealRoutes.Restrictions, new RestrictionCreatedResponse(result.Value)) : errors.ToResult(result.Error!);
    }
}
