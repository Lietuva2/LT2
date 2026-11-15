using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Me;
using Lt2.Appeals;
using Lt2.Notifications;
using Lt2.SharedKernel;

namespace Lt2.Web.Services;

/// <summary>
/// The caller's own side and follow for the bills a page shows (design 8.7), composed by the host from the Appeals module
/// (the person's own sides, one read) and the Notifications module (the bills they follow, one read). Nothing about anyone
/// else; never cached anywhere (the answer is under <c>/api</c>, which says <c>no-store</c>).
/// </summary>
internal sealed class MyBillsReads(IAppealsModule appeals, IFollows follows)
{
    public async Task<MyBillsResponse> ReadAsync(Guid userId, IReadOnlyList<string> numbers, CancellationToken ct)
    {
        var sides = await appeals.MySidesAsync(userId, numbers, ct);
        var followed = await follows.FollowedBillNumbersAsync(userId, ct);
        var rows = new List<MyBillResponse>();
        foreach (var number in numbers.Select(n => n.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal))
        {
            var side = sides.GetValueOrDefault(number);
            var following = followed.Contains(number);
            if (side is not null || following)
            {
                rows.Add(new MyBillResponse(number, side, following));
            }
        }

        return new MyBillsResponse(rows);
    }
}

/// <summary>
/// Server-side <see cref="IMyBillsApi"/>. The front page's member island asks only in the browser, so this answers only a
/// component that asks on the server by mistake: with the request's own account, and nothing for a visitor.
/// </summary>
internal sealed class ServerMyBillsApi(MyBillsReads reads, IHttpContextAccessor accessor) : IMyBillsApi
{
    public async Task<ApiResult<MyBillsResponse>> GetAsync(MyBillsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return accessor.HttpContext?.User.UserId() is { } id
            ? ApiResult<MyBillsResponse>.Ok(await reads.ReadAsync(id, query.Numbers, cancellationToken))
            : ApiResult<MyBillsResponse>.Fail(new ApiProblem { Status = 401, Code = "auth.required" });
    }
}
