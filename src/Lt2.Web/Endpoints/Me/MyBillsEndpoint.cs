using System.Security.Claims;
using FluentValidation;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Me;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Me;

/// <summary>
/// <c>GET /api/v1/me/bills?numbers=A,B</c> (design 8.7): the caller's own side and follow for the bills the front page
/// shows, in one request instead of one per bill. A member's own answer: never cached (as every <c>/api</c> answer that
/// does not say otherwise), nothing about anyone else, at most <see cref="MyBillsQuery.MaxNumbers"/> bills, and limited per
/// account like the other answers a page asks often.
/// </summary>
internal static class MyBillsEndpoint
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet(ApiEndpoints.Relative(ApiRoutes.Me.Bills), HandleAsync)
            .WithName("GetMyBills")
            .WithTags("Me")
            .RequireAuthorization(Policies.Member)
            .RequireRateLimiting(RateLimitPolicies.OwnRead)
            .Produces<MyBillsResponse>()
            .ProducesValidationProblem();

        // Design 8.11.8: a member opened a bill's page. A write (antiforgery and the terms gate like every one), sent once per page
        // from the browser; under the own-answers limit, as it is the page's own answer about the person.
        api.MapPost(ApiEndpoints.Relative(ApiRoutes.Me.BillSeen), async (string number, ClaimsPrincipal user, MyBillsReads reads, CancellationToken ct) =>
                TypedResults.Ok(await reads.SeenAsync(user.UserId()!.Value, number, ct)))
            .WithName("SeenBill")
            .WithTags("Me")
            .RequireAuthorization(Policies.Member)
            .RequireRateLimiting(RateLimitPolicies.OwnRead)
            .Produces<BillSeenResponse>();
    }

    private static async Task<IResult> HandleAsync(
        string? numbers, ClaimsPrincipal user, IValidator<MyBillsQuery> validator, MyBillsReads reads, ErrorMapper errors, CancellationToken ct)
    {
        var query = MyBillsQuery.Parse(numbers);
        var checkedQuery = await validator.ValidateAsync(query, ct);
        if (!checkedQuery.IsValid)
        {
            // Any error is about the one query parameter, as the API names it.
            return errors.ValidationFailed(new Dictionary<string, string[]> { ["numbers"] = checkedQuery.Errors.Select(e => e.ErrorMessage).Distinct().ToArray() });
        }

        return TypedResults.Ok(await reads.ReadAsync(user.UserId()!.Value, query.Numbers, ct));
    }
}
