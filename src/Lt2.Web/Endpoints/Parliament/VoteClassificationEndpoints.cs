using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Parliament;
using Lt2.Parliament;
using Lt2.Web.Endpoints.Moderation;
using Lt2.Web.Security;
using Lt2.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Lt2.Web.Endpoints.Parliament;

/// <summary>
/// Design 7.2: a moderator corrects a vote's classification, for a moderator or an administrator in a staff-passkey session.
/// <c>GET /api/v1/moderation/votes/{id}/classification</c> is the state the vote page's "Moderavimas" part needs (the
/// classification in force, the rules' own, the correction's time and statement, whether the vote closed a record);
/// <c>PUT</c> corrects the kind and a stage result's stage; <c>DELETE</c> returns to the rules' classification. Each write needs
/// a statement and is audited; correcting the closing vote of a record out of the adoption stage is refused
/// (<c>vote-classification.closing-vote</c>, 409).
/// </summary>
internal static class VoteClassificationEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet(ApiEndpoints.Relative(ParliamentRoutes.VoteClassification), GetAsync)
            .WithName("GetVoteClassification")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .Produces<VoteClassificationResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapPut(ApiEndpoints.Relative(ParliamentRoutes.VoteClassification), CorrectAsync)
            .WithName("CorrectVoteClassification")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .WithValidation<VoteClassificationRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapDelete(ApiEndpoints.Relative(ParliamentRoutes.VoteClassification), ResetAsync)
            .WithName("ResetVoteClassification")
            .WithTags("Moderation")
            .RequireAuthorization(Policies.Moderator)
            .WithValidation<VoteClassificationResetRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> GetAsync(long id, IVoteClassifications classifications, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var result = await classifications.StateAsync(id, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(ParliamentReads.Map(result.Value)) : errors.ToResult(result.Error!);
    }

    private static Task<IResult> CorrectAsync(long id, VoteClassificationRequest request, HttpContext context, IVoteClassifications classifications, CancellationToken cancellationToken) =>
        ModeratorEndpoint.ActAsync(context, m => classifications.CorrectAsync(id, request.Kind, request.Stage, request.Statement, m, cancellationToken));

    private static Task<IResult> ResetAsync(long id, [FromBody] VoteClassificationResetRequest request, HttpContext context, IVoteClassifications classifications, CancellationToken cancellationToken) =>
        ModeratorEndpoint.ActAsync(context, m => classifications.ResetAsync(id, request.Statement, m, cancellationToken));
}
