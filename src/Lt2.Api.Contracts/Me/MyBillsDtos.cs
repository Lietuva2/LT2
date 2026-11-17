using FluentValidation;
using Microsoft.Extensions.Localization;

namespace Lt2.Api.Contracts.Me;

// Design 8.7 (the front page): the caller's own side and follow for the bills a page shows, in one never-cached answer.
// It holds nothing about anyone else, and nothing but the bills asked about.

/// <summary>
/// One bill the caller asked about: their own side (<c>for</c>, <c>against</c>, or none) and whether they follow it; on a followed
/// bill, what was written there since they last opened its page (design 8.11.8), null when nothing.
/// </summary>
public sealed record MyBillResponse(string Number, string? Side, bool Following, BillNewsCountsResponse? New = null);

/// <summary>
/// How many texts on a followed bill are new to the caller (design 8.11.8): arguments, comments, organisations' statements and
/// MPs' responses made public or changed since they last opened its page; never their own.
/// </summary>
public sealed record BillNewsCountsResponse(int Reasons, int Comments, int Statements, int Responses);

/// <summary>A comment new to the caller (design 8.11.8) and the text it stands under (<c>reason</c>, <c>statement</c>, <c>response</c>).</summary>
public sealed record NewCommentResponse(Guid Id, string TargetKind, Guid TargetId);

/// <summary>
/// <c>POST /api/v1/me/bills/{number}/seen</c> (design 8.11.8): the caller opened the bill's page. On a bill they follow the moment
/// is kept and <c>Since</c> is the one before it, with the texts written after it by id (never their own); on a bill they do not
/// follow nothing is kept, <c>Following</c> is false and the lists are empty.
/// </summary>
public sealed record BillSeenResponse(
    bool Following,
    DateTimeOffset? Since,
    IReadOnlyList<Guid> Reasons,
    IReadOnlyList<NewCommentResponse> Comments,
    IReadOnlyList<Guid> Statements,
    IReadOnlyList<Guid> Responses)
{
    public static readonly BillSeenResponse NotFollowing = new(false, null, [], [], [], []);
}

/// <summary>
/// <c>GET /api/v1/me/bills?numbers=…</c>: one row for each asked bill the caller has a side on or follows, in the order
/// asked; a bill with neither is left out. <c>HasPositionConsent</c>: the caller has given the positions consent (design
/// 6.5), so a side taken in a row of the front page is saved at once; without it the row asks for the consent first.
/// <c>MpAccount</c>: the caller is an MP's own linked account (design 8.5.4, owner 2026-11-17), which takes no side, so the
/// rows offer none; an office member's is false. Asked only in the browser, so no served page holds it.
/// </summary>
public sealed record MyBillsResponse(IReadOnlyList<MyBillResponse> Bills, bool HasPositionConsent, bool MpAccount = false);

/// <summary>The bills asked about in one request.</summary>
public sealed record MyBillsQuery
{
    /// <summary>At most this many bills in one request: the front page shows at most eight planned and a week's votes.</summary>
    public const int MaxNumbers = 30;

    /// <summary>A bill number's longest form, for example <c>XIVP-1234(2)</c> with room to spare.</summary>
    public const int NumberMaxLength = 40;

    public IReadOnlyList<string> Numbers { get; init; } = [];

    /// <summary>Reads <c>numbers=A,B</c>: comma-separated, spaces around each ignored, empty parts dropped, repeats once.</summary>
    public static MyBillsQuery Parse(string? numbers) => new()
    {
        Numbers = (numbers ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
    };

    public string ToQueryString() => "numbers=" + Uri.EscapeDataString(string.Join(",", Numbers));
}

/// <summary>One to <see cref="MyBillsQuery.MaxNumbers"/> bill numbers, each short and of the characters bill numbers use.</summary>
public sealed class MyBillsQueryValidator : AbstractValidator<MyBillsQuery>
{
    public MyBillsQueryValidator(IStringLocalizer<ValidationMessages> l)
    {
        ArgumentNullException.ThrowIfNull(l);
        RuleFor(x => x.Numbers).Must(n => n.Count is > 0 and <= MyBillsQuery.MaxNumbers).WithMessage(l["MyBills.Count"]);
        RuleForEach(x => x.Numbers)
            .Must(n => n.Length <= MyBillsQuery.NumberMaxLength && n.All(c => char.IsLetterOrDigit(c) || c is '-' or '/' or '(' or ')' or '.'))
            .WithMessage(l["MyBills.Number"]);
    }
}

/// <summary>The caller's own answers about several bills at once (design 8.7); in the browser, plain HTTP.</summary>
public interface IMyBillsApi
{
    Task<ApiResult<MyBillsResponse>> GetAsync(MyBillsQuery query, CancellationToken cancellationToken = default);

    /// <summary>The caller opened the bill's page (design 8.11.8): sent once per page, only from the browser.</summary>
    Task<ApiResult<BillSeenResponse>> SeenAsync(string number, CancellationToken cancellationToken = default);
}
