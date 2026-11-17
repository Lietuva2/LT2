namespace Lt2.SharedKernel;

/// <summary>
/// How many texts on a followed bill are new to one member (design 8.11.8): arguments, comments, organisations' statements and
/// MPs' responses made public or changed after the member last opened the bill's page; never their own, never hidden ones.
/// </summary>
public sealed record BillNewsCounts(int Reasons, int Comments, int Statements, int Responses)
{
    public static readonly BillNewsCounts None = new(0, 0, 0, 0);

    public bool Any => Reasons + Comments + Statements + Responses > 0;
}

/// <summary>A comment new to the member, with the text it stands under (<c>reason</c>, <c>statement</c>, <c>response</c>).</summary>
public sealed record NewComment(Guid Id, string TargetKind, Guid TargetId);

/// <summary>The texts on one bill new to one member (design 8.11.8), by id, for the marks on the bill's page.</summary>
public sealed record BillNews(IReadOnlyList<Guid> Reasons, IReadOnlyList<NewComment> Comments, IReadOnlyList<Guid> Statements, IReadOnlyList<Guid> Responses)
{
    public static readonly BillNews None = new([], [], [], []);

    public BillNewsCounts Counts => new(Reasons.Count, Comments.Count, Statements.Count, Responses.Count);
}

/// <summary>
/// What is new on the bills a member follows (design 8.11.8), implemented by Appeals: the texts its bill's page shows that were
/// made public or changed after the given moment, the member's own excluded (what they wrote, their organisations' statements and
/// comments, the responses and comments of the MP they speak for). Asked by the host with the moments Notifications keeps on the
/// member's follows; the answer is the member's own and is never kept.
/// </summary>
public interface IBillNews
{
    /// <summary>One bill's new texts since <paramref name="since"/>, for the marks on its page.</summary>
    Task<BillNews> OnBillAsync(Guid userId, string billNumber, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>
    /// The counts for many bills, each since its own moment, in a fixed number of queries whatever the number of bills; a bill
    /// with nothing new is left out.
    /// </summary>
    Task<IReadOnlyDictionary<string, BillNewsCounts>> CountsAsync(Guid userId, IReadOnlyDictionary<string, DateTimeOffset> since, CancellationToken cancellationToken);
}
