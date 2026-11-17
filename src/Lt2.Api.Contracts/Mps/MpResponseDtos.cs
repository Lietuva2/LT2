using FluentValidation;
using Lt2.Api.Contracts.Appeals;
using Microsoft.Extensions.Localization;

namespace Lt2.Api.Contracts.Mps;

// Design 8.5.5 to 8.5.7, 8.8: an MP's response on a bill. Codes: stance for | against | undecided; status published |
// withdrawn | hidden; ballot for | against | abstain | did-not-vote | consensus. No answer here says which account wrote a
// response or whether the MP or an office member did: every response carries the one mark "Seimo narys arba jo biuras".
// Nothing adds responses and ballots up: there is no share, score or ranking of answers against ballots (design 7.1).

/// <summary>The routes of design 8.8 for responses, and the page of 8.5.7.</summary>
public static class MpResponseRoutes
{
    /// <summary><c>GET</c>, public: the responses on a bill.</summary>
    public const string OnBill = ApiRoutes.Prefix + "/bills/{number}/responses";

    /// <summary><c>GET</c> the linked account's own response and form state; <c>PUT</c> publish or change; <c>DELETE</c> withdraw.</summary>
    public const string Mine = OnBill + "/mine";

    /// <summary><c>GET</c>, public: an MP's responses, for the profile's "Atsakymai".</summary>
    public const string OfMp = ApiRoutes.Prefix + "/mps/{slug}/responses";

    /// <summary><c>GET</c>: the bills in summaries now, with the MP's response (design 8.5.7). A linked account only.</summary>
    public const string MyBills = ApiRoutes.Prefix + "/me/mp/bills";

    /// <summary><c>GET</c>, moderators: the responses in the queue and the hidden ones.</summary>
    public const string Moderation = ApiRoutes.Prefix + "/moderation/responses";

    public const string Keep = Moderation + "/{id:guid}/keep";
    public const string Hide = Moderation + "/{id:guid}/hide";
    public const string Restore = Moderation + "/{id:guid}/restore";

    /// <summary>The page of design 8.5.7, "Projektai, dėl kurių į jus kreipiamasi".</summary>
    public const string BillsPage = "/seimo-narys/projektai";

    public static string OnBillOf(string number) => $"{ApiRoutes.Prefix}/bills/{Uri.EscapeDataString(number)}/responses";

    public static string MineOf(string number) => OnBillOf(number) + "/mine";

    public static string OfMpOf(string slug) => $"{ApiRoutes.Prefix}/mps/{Uri.EscapeDataString(slug)}/responses";

    public static string With(string route, Guid id) => route.Replace("{id:guid}", id.ToString(), StringComparison.Ordinal);
}

/// <summary>The response's limits, the same numbers the server enforces (design 8.5.5), and the lists' paging (8.5.6).</summary>
public static class MpResponseLimits
{
    public static readonly IReadOnlyList<string> Stances = ["for", "against", "undecided"];

    /// <summary>The responses a list shows first, on the bill's page and on the profile.</summary>
    public const int First = 10;

    /// <summary>The responses "Rodyti daugiau" adds.</summary>
    public const int Step = 10;

    /// <summary>The page's address parameter of the list's length (<c>?atsakymai=</c>).</summary>
    public const string ShowParameter = "atsakymai";
}

/// <summary>An earlier public version: its stance and text, when written, when replaced, and whether it was the text withdrawn.</summary>
public sealed record MpResponseVersionResponse(string Stance, string Text, DateTimeOffset WrittenAt, DateTimeOffset ReplacedAt, bool Withdrawn);

/// <summary>After the adoption vote: the MP's ballot at it, a fact beside the response, never compared with it.</summary>
public sealed record MpResponseVoteResponse(long VoteId, DateTimeOffset HeldAt, string Ballot);

/// <summary>
/// A response as the public sees it. While <c>hidden</c> only the moderator's statement is given; while <c>withdrawn</c> the
/// stance and text are null and the versions hold what was said; <see cref="Vote"/> only after the adoption vote.
/// </summary>
public sealed record MpResponseResponse(
    Guid Id,
    string Status,
    string? Stance,
    string? Text,
    DateTimeOffset PublishedAt,
    DateTimeOffset? EditedAt,
    DateTimeOffset? WithdrawnAt,
    string? HiddenStatement,
    IReadOnlyList<MpResponseVersionResponse> Earlier,
    MpResponseVoteResponse? Vote)
{
    /// <summary>How many comments the response shows on the bill's page ("Aptarimas (n)", design 8.23.6, 8.23.14).</summary>
    public int Comments { get; init; }
}

/// <summary>The MP who answered: public facts from open data. The faction is the current one, null without one.</summary>
public sealed record MpResponderResponse(string Slug, string FirstName, string LastName, string? Faction);

public sealed record BillMpResponseResponse(MpResponderResponse Mp, MpResponseResponse Response);

/// <summary>How many MPs answered (published responses) and how they split; withdrawn and hidden ones are not counted.</summary>
public sealed record MpResponseCountsResponse(int Answered, int For, int Against, int Undecided);

/// <summary>
/// The responses on a bill (design 8.5.6), in the page's order: "už", "prieš", "neapsisprendė", each by last name, then
/// withdrawn and hidden ones. <c>Open</c>: responses can still be written.
/// </summary>
public sealed record BillMpResponsesResponse(string BillNumber, bool Open, MpResponseCountsResponse Counts, IReadOnlyList<BillMpResponseResponse> Items);

/// <summary>One answered bill on the MP's profile.</summary>
public sealed record MpAnswerResponse(BillRefResponse Bill, bool BillOpen, MpResponseResponse Response);

/// <summary>The MP's responses, the most recently written first (design 8.5.6, the profile's "Atsakymai").</summary>
public sealed record MpAnswersResponse(string Slug, IReadOnlyList<MpAnswerResponse> Items);

/// <summary>
/// The linked account's form on a bill's page (design 8.5.6): whether a response can be written now (<see cref="Open"/>),
/// whether this session may write it (<see cref="MayWrite"/>: a passkey session, the mandate not ended), why not
/// (<see cref="Refusal"/>, a problem code), and the MP's response if any. Only in the account's own answer.
/// </summary>
public sealed record MyMpResponseResponse(string BillNumber, MpLinkResponse Mp, bool Open, bool MayWrite, string? Refusal, MpResponseResponse? Response);

public sealed record MpResponseRequest
{
    public string Stance { get; init; } = "";

    public string Text { get; init; } = "";
}

public sealed record MpResponseSavedResponse(Guid Id, string Status);

/// <summary>
/// A bill on the page of design 8.5.7: what the summary would say about it (number, title, the planned date and stage, both
/// sides with the counts for accounts older than 30 days) and the MP's own response, if any. A bill the MP answered that is in
/// no summary now (owner, 2026-11-17) has no counts (null) and says whether it is still open for an answer.
/// </summary>
public sealed record MpBillResponse(
    string Number, string Title, PlannedDateResponse? Planned, int? For, int? ForOlderThan30Days, int? Against, int? AgainstOlderThan30Days,
    MpResponseResponse? Response, bool Open = true);

/// <summary>
/// "Projektai, dėl kurių į jus kreipiamasi" (design 8.5.7): the summaries' list, live, for the MP the account speaks for, then
/// the bills the MP answered that are not in it, newest answer first (owner, 2026-11-17).
/// </summary>
public sealed record MpBillsResponse(MpLinkResponse Mp, IReadOnlyList<MpBillResponse> Items);

/// <summary>A response in the moderators' queue: flagged and not yet seen, or reported. Never who wrote it.</summary>
public sealed record QueueMpResponseResponse(
    Guid Id, BillRefResponse Bill, MpResponderResponse Mp, string Status, string Stance, string Text, IReadOnlyList<string> Flags,
    IReadOnlyList<QueueReportResponse> Reports, DateTimeOffset PublishedAt, DateTimeOffset? EditedAt, bool BillOpen);

public sealed record HiddenMpResponseResponse(
    Guid Id, BillRefResponse Bill, MpResponderResponse Mp, string Stance, string Text, DateTimeOffset? HiddenAt, string? HiddenBy, string? Statement, bool Restorable);

public sealed record MpResponseModerationResponse(IReadOnlyList<QueueMpResponseResponse> Queue, IReadOnlyList<HiddenMpResponseResponse> Hidden);

public sealed class MpResponseRequestValidator : AbstractValidator<MpResponseRequest>
{
    public MpResponseRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.Stance).Must(s => MpResponseLimits.Stances.Contains(s)).WithMessage(l["MpResponse.Stance"]);
        RuleFor(x => x.Text).Cascade(CascadeMode.Stop)
            .Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage(l["MpResponse.Required"])
            .Must(t => t.Trim().Length <= MemberTextLimits.Ceiling).WithMessage(l["MpResponse.TooLong", MemberTextLimits.Number(MemberTextLimits.Ceiling)]); // the setting Texts:MaxLength: where it is written
    }
}

/// <summary>Design 8.5.5 to 8.5.7: reading and writing MPs' responses.</summary>
public interface IMpResponsesApi
{
    /// <summary>Public: the responses on a bill, in the page's order.</summary>
    Task<ApiResult<BillMpResponsesResponse>> GetOnBillAsync(string number, CancellationToken cancellationToken = default);

    /// <summary>Public: an MP's responses, newest first.</summary>
    Task<ApiResult<MpAnswersResponse>> GetOfMpAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>The linked account's own response on a bill and whether it may write.</summary>
    Task<ApiResult<MyMpResponseResponse>> GetMineAsync(string number, CancellationToken cancellationToken = default);

    Task<ApiResult<MpResponseSavedResponse>> SaveAsync(string number, MpResponseRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> WithdrawAsync(string number, CancellationToken cancellationToken = default);

    /// <summary>Design 8.5.7: the bills in summaries now, with the MP's responses.</summary>
    Task<ApiResult<MpBillsResponse>> GetMyBillsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Moderators: responses in the queue, hidden ones, and the actions.</summary>
public interface IMpResponseModerationApi
{
    Task<ApiResult<MpResponseModerationResponse>> GetAsync(CancellationToken cancellationToken = default);

    Task<ApiResult> KeepAsync(Guid id, ModerationRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> HideAsync(Guid id, StatementRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> RestoreAsync(Guid id, StatementRequest request, CancellationToken cancellationToken = default);
}
