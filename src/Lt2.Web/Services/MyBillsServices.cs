using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Me;
using Lt2.Appeals;
using Lt2.Notifications;
using Lt2.SharedKernel;

namespace Lt2.Web.Services;

/// <summary>
/// The caller's own side and follow for the bills a page shows (design 8.7), composed by the host from the Appeals module
/// (the person's own sides, one read), the Notifications module (the bills they follow, one read) and the Identity module
/// through <see cref="IParticipants"/> (whether they gave the positions consent, so the front page's rows know whether to
/// ask for it, and whether it is an MP's own account, which takes no side: design 8.5.4). Nothing about anyone else; never cached anywhere (the answer is under <c>/api</c>, which says <c>no-store</c>).
/// </summary>
internal sealed class MyBillsReads(IAppealsModule appeals, IFollows follows, IParticipants participants, IBillNews news)
{
    public async Task<MyBillsResponse> ReadAsync(Guid userId, IReadOnlyList<string> numbers, CancellationToken ct)
    {
        var asked = numbers.Select(n => n.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToList();
        var sides = await appeals.MySidesAsync(userId, numbers, ct);
        var followed = await follows.FollowedBillsAsync(userId, ct);
        var facts = await participants.FindAsync(userId, ct);
        var consent = facts?.HasPositionConsent == true;

        // Design 8.11.8: what is new on the asked bills the person follows, each since their last visit, in one read.
        var since = asked.Where(followed.ContainsKey).ToDictionary(n => n, n => followed[n], StringComparer.Ordinal);
        var counts = since.Count == 0 ? new Dictionary<string, BillNewsCounts>() : await news.CountsAsync(userId, since, ct);
        var rows = new List<MyBillResponse>();
        foreach (var number in asked)
        {
            var side = sides.GetValueOrDefault(number);
            var following = followed.ContainsKey(number);
            if (side is not null || following)
            {
                rows.Add(new MyBillResponse(number, side, following, Map(counts.GetValueOrDefault(number))));
            }
        }

        return new MyBillsResponse(rows, consent, facts?.IsMpAccount == true);
    }

    /// <summary>
    /// The person opened the bill's page (design 8.11.8): on a followed bill the moment is kept and the texts written after the one
    /// before are answered by id; on another bill nothing is kept and nothing answered.
    /// </summary>
    public async Task<BillSeenResponse> SeenAsync(Guid userId, string number, CancellationToken ct)
    {
        if (await follows.SeenBillAsync(userId, number, ct) is not { } since)
        {
            return BillSeenResponse.NotFollowing;
        }

        var found = await news.OnBillAsync(userId, number, since, ct);
        return new BillSeenResponse(
            true,
            since,
            found.Reasons,
            found.Comments.Select(c => new NewCommentResponse(c.Id, c.TargetKind, c.TargetId)).ToList(),
            found.Statements,
            found.Responses);
    }

    /// <summary>The counts as the lists show them; null when nothing is new.</summary>
    public static BillNewsCountsResponse? Map(BillNewsCounts? counts) =>
        counts is { Any: true } c ? new BillNewsCountsResponse(c.Reasons, c.Comments, c.Statements, c.Responses) : null;
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

    /// <summary>Never sent while the server prerenders (design 8.11.8): a component that asks here by mistake gets the refusal.</summary>
    public Task<ApiResult<BillSeenResponse>> SeenAsync(string number, CancellationToken cancellationToken = default) =>
        Task.FromResult(ApiResult<BillSeenResponse>.Fail(new ApiProblem { Status = 400, Code = "seen.browser-only" }));
}
