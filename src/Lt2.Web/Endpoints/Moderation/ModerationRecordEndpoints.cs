using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Moderation;

/// <summary>
/// A moderator's controls on the bill's page (design 8.12.3), for a moderator or an administrator in a staff-passkey
/// session: <c>GET /moderation/bills/{number}</c> is the state that decides which actions apply (also for a bill without a
/// record); <c>POST …/record</c> finds or creates the bill's record for an action on it (design 8.12.1); <c>PUT
/// …/description</c> writes or corrects the description of any active bill, with a required statement that is audited;
/// <c>POST …/description/confirm</c> confirms an AI summary nobody has read and <c>POST …/description/remove</c> removes a
/// description (design 8.13);
/// <c>GET /moderation/ai</c> says whether an AI draft can be asked for and <c>POST …/description-draft</c> asks for one
/// from pasted official text (design 8.11.4; refused while no provider is configured).
/// </summary>
internal static class ModerationRecordEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet(ApiEndpoints.Relative(AppealRoutes.ModerationBill), GetAsync)
            .WithName("GetModerationBill")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .Produces<ModerationBillResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapPost(ApiEndpoints.Relative(AppealRoutes.BillRecord), EnsureRecordAsync)
            .WithName("EnsureBillRecord")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .Produces<BillRecordIdResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapPut(ApiEndpoints.Relative(AppealRoutes.BillDescription), DescribeAsync)
            .WithName("WriteBillDescription")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .WithValidation<ModeratorDescriptionRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapPost(ApiEndpoints.Relative(AppealRoutes.ConfirmDescription), ConfirmAsync)
            .WithName("ConfirmBillDescription")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .WithValidation<ModerationRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapPost(ApiEndpoints.Relative(AppealRoutes.RemoveDescription), RemoveAsync)
            .WithName("RemoveBillDescription")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .WithValidation<StatementRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapGet(ApiEndpoints.Relative(AppealRoutes.Ai), (IAppealsModeration moderation) => TypedResults.Ok(new AiStatusResponse(moderation.DraftAvailable)))
            .WithName("GetAiStatus")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .Produces<AiStatusResponse>();

        api.MapPost(ApiEndpoints.Relative(AppealRoutes.DescriptionDraft), DraftAsync)
            .WithName("DraftDescription")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .WithValidation<DescriptionDraftRequest>()
            .Produces<DescriptionDraftResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> GetAsync(string number, IAppealsModeration moderation, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await moderation.BillStateAsync(number, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(AppealMapping.Map(result.Value!)) : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> EnsureRecordAsync(string number, IAppealsModeration moderation, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await moderation.EnsureRecordAsync(number, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(new BillRecordIdResponse(result.Value)) : errors.ToResult(result.Error!);
    }

    private static Task<IResult> DescribeAsync(string number, ModeratorDescriptionRequest request, HttpContext context, IAppealsModeration moderation, CancellationToken cancellationToken) =>
        ModeratorEndpoint.ActAsync(context, m => moderation.WriteDescriptionAsync(number, AppealMapping.ToModule(request), m, cancellationToken));

    private static Task<IResult> ConfirmAsync(string number, ModerationRequest request, HttpContext context, IAppealsModeration moderation, CancellationToken cancellationToken) =>
        ModeratorEndpoint.ActAsync(context, m => moderation.ConfirmDescriptionAsync(number, request.Statement, m, cancellationToken));

    private static Task<IResult> RemoveAsync(string number, StatementRequest request, HttpContext context, IAppealsModeration moderation, CancellationToken cancellationToken) =>
        ModeratorEndpoint.ActAsync(context, m => moderation.RemoveDescriptionAsync(number, request.Statement, m, cancellationToken));

    private static async Task<IResult> DraftAsync(
        string number, DescriptionDraftRequest request, HttpContext context, IAppealsModeration moderation, ErrorMapper errors, CancellationToken ct)
    {
        var moderator = await ModeratorEndpoint.ModeratorAsync(context);
        var result = await moderation.DraftDescriptionAsync(number, request.SourceText, moderator, ct);
        return result.IsSuccess ? TypedResults.Ok(new DescriptionDraftResponse(result.Value)) : errors.ToResult(result.Error!);
    }
}
