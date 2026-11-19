using FluentValidation;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Mps;
using Lt2.Api.Contracts.Organisations;
using Microsoft.Extensions.Localization;

namespace Lt2.Api.Contracts.Comments;

// Design 8.23: comments ("Aptarimas") under arguments, organisations' statements and MPs' responses. Codes: kind reason |
// statement | response; voice person | organisation | mp; status published | hidden. No answer carries an account's id, and
// nothing ties a comment or a "Sutinku" to a side. The caller's own answer (MyCommentsResponse) is never cached and read in
// the browser only.

/// <summary>The routes of design 8.23.11.</summary>
public static class CommentRoutes
{
    /// <summary>
    /// <c>GET</c> (public) the shown comments of a target, oldest first, a page at a time: <c>limit</c> of them (the first page
    /// at least <see cref="CommentLimits.First"/>) from <c>cursor</c> on (<see cref="CommentThreadResponse.NextCursor"/>);
    /// <c>POST</c> a member writes one.
    /// </summary>
    public const string Thread = ApiRoutes.Prefix + "/comments/{kind}/{targetId:guid}";

    /// <summary><c>DELETE</c>: whoever may delete it, while the bill is open; <c>PUT</c>: its author edits it until someone agrees.</summary>
    public const string One = ApiRoutes.Prefix + "/comments/{id:guid}";

    /// <summary><c>PUT</c> "Sutinku", <c>DELETE</c> takes it back.</summary>
    public const string Agreement = One + "/agreement";

    /// <summary><c>GET</c> (public): where a shown comment stands, for a link to it (design 8.23.6).</summary>
    public const string Place = One + "/place";

    /// <summary><c>GET</c> the caller's own answer about the comments on a bill (never cached).</summary>
    public const string Mine = ApiRoutes.Prefix + "/bills/{number}/comments/mine";

    /// <summary><c>GET</c>, moderators: reported comments and hidden ones.</summary>
    public const string Moderation = ApiRoutes.Prefix + "/moderation/comments";

    public const string Hide = Moderation + "/{id:guid}/hide";
    public const string Restore = Moderation + "/{id:guid}/restore";
    public const string Keep = Moderation + "/{id:guid}/keep";
    public const string Writer = Moderation + "/{id:guid}/writer";
    public const string RestrictWriter = Moderation + "/{id:guid}/restrict-writer";

    /// <summary><c>GET</c>, moderators: the bill page's answer.</summary>
    public const string OnBill = ApiRoutes.Prefix + "/moderation/bills/{number}/comments";

    /// <summary>The thread's address: <paramref name="limit"/> comments from <paramref name="cursor"/> on (both optional).</summary>
    public static string ThreadOf(string kind, Guid targetId, int? limit = null, string? cursor = null)
    {
        var query = new List<string>();
        if (!string.IsNullOrEmpty(cursor))
        {
            query.Add("cursor=" + Uri.EscapeDataString(cursor));
        }

        if (limit is { } n)
        {
            query.Add("limit=" + n.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return $"{ApiRoutes.Prefix}/comments/{Uri.EscapeDataString(kind)}/{targetId}" + (query.Count == 0 ? "" : "?" + string.Join("&", query));
    }

    public static string OneOf(Guid id) => $"{ApiRoutes.Prefix}/comments/{id}";

    public static string PlaceOf(Guid id) => OneOf(id) + "/place";

    public static string AgreementOf(Guid id) => OneOf(id) + "/agreement";

    public static string MineOf(string number) => $"{ApiRoutes.Prefix}/bills/{Uri.EscapeDataString(number)}/comments/mine";

    public static string OnBillOf(string number) => $"{ApiRoutes.Prefix}/moderation/bills/{Uri.EscapeDataString(number)}/comments";

    public static string With(string route, Guid id) => route.Replace("{id:guid}", id.ToString(), StringComparison.Ordinal);
}

/// <summary>The comments' fixed numbers (design 8.23.6) and their page parameter (8.23.14).</summary>
public static class CommentLimits
{
    public static readonly IReadOnlyList<string> Kinds = ["reason", "statement", "response"];

    /// <summary>The comments a thread shows first.</summary>
    public const int First = 10;

    /// <summary>The comments "Rodyti daugiau komentarų" adds (owner, 2026-11-19: a list of texts, 20 a press).</summary>
    public const int Step = ListPaging.TextStep;

    /// <summary>The page's address parameter: the target's id (32 hexadecimal digits), a hyphen, how many to show.</summary>
    public const string ShowParameter = "komentarai";

    /// <summary>The element id of a comment: its stable anchor (design 8.23.6). A link to it is <see cref="BillTextLinks.ToComment"/>.</summary>
    public static string ElementId(Guid id) => BillTextLinks.CommentElement(id);
}

/// <summary>
/// Where a shown comment stands (design 8.23.6), for the bill's page resolving <c>?komentaras=</c>: its target's kind and id, its
/// bill, and its position among the target's shown comments, oldest first (1-based). A comment not shown has no place
/// (<c>comment.not-found</c>, the same for hidden and unknown ones).
/// </summary>
public sealed record CommentPlaceResponse(Guid CommentId, string Kind, Guid TargetId, string BillNumber, int Position);

/// <summary>
/// The name a comment is shown under (design 8.23.2). <c>person</c>: <see cref="Name"/> is the display name, or null
/// ("Lietuva 2.0 naudotojas"); <c>organisation</c>: the organisation's name, profile and label; <c>mp</c>: the MP. Never
/// the person behind an organisation's or an MP's comment. <c>ProfileId</c>: for a person with a public profile (design
/// 8.25.4), the name links to <c>/nariai/{id}</c> with its card; null for everyone else and for the other voices.
/// </summary>
public sealed record CommentAuthorResponse(string Voice, string? Name, string? OrganisationSlug, OrganisationLabelResponse? Label, MpResponderResponse? Mp, string? ProfileId = null);

/// <summary>One who agreed (design 8.23.10): the display name (null: "Lietuva 2.0 naudotojas") and the public profile id when they have one.</summary>
public sealed record CommentAgreerResponse(string? Name, string? ProfileId = null);

/// <summary>A shown comment: its name, time, text and who agreed (oldest first).</summary>
public sealed record CommentResponse(Guid Id, CommentAuthorResponse Author, string Text, DateTimeOffset CreatedAt, IReadOnlyList<CommentAgreerResponse> AgreedBy);

/// <summary>
/// The shown comments of one target, oldest first: <see cref="Count"/> in all, one page of them in <see cref="Items"/>, and
/// <see cref="NextCursor"/> where the next page starts (null after the last).
/// <c>Open</c>: a member may write here now; <c>BillOpen</c>: "Sutinku" and deleting still work. <c>Cards</c>: once per person
/// with a public profile among the names shown (design 8.25.4), no follower number.
/// </summary>
public sealed record CommentThreadResponse(
    string Kind, Guid TargetId, string BillNumber, int Count, bool Open, bool BillOpen, IReadOnlyList<CommentResponse> Items, IReadOnlyList<AuthorCardResponse>? Cards = null,
    string? NextCursor = null);

/// <summary>The caller's voice under one target where it is not a person's (<c>organisation</c>, <c>mp</c>), or why not (a code).</summary>
public sealed record CommentTargetVoiceResponse(string Kind, Guid TargetId, string? Voice, string? Refusal);

/// <summary>
/// The caller's own answer about a bill's comments (design 8.23.6, never cached): what they may delete, what they wrote, what
/// they agreed with, whether they ever agreed (the first "Sutinku" asks), their voices, a writing restriction, whether the
/// account is an MP's own, and the ceiling per text (0 = none) with how many shown comments they have under each target.
/// </summary>
public sealed record MyCommentsResponse(
    bool AgreedBefore,
    bool Restricted,
    bool MpAccount,
    IReadOnlyList<Guid> Deletable,
    IReadOnlyList<Guid> Written,
    IReadOnlyList<Guid> Agreed,
    IReadOnlyList<CommentTargetVoiceResponse> Voices,
    int PerTargetLimit,
    IReadOnlyDictionary<Guid, int> WrittenPerTarget)
{
    /// <summary>The caller's own comments they may edit now (design 8.23.3, owner 2026-11-17: until someone says "Sutinku").</summary>
    public IReadOnlyList<Guid> Editable { get; init; } = [];

    /// <summary>The caller's own comments in any voice: no "Pranešti" there (owner, 2026-11-17).</summary>
    public IReadOnlyList<Guid> Own { get; init; } = [];

    /// <summary>The caller's own comments a moderator hid, with the moderator's statement (owner, 2026-11-17). Never in a public answer.</summary>
    public IReadOnlyList<MyHiddenCommentResponse> Hidden { get; init; } = [];
}

/// <summary>One's own hidden comment: under which target, its text and time, and the moderator's statement when there is one.</summary>
public sealed record MyHiddenCommentResponse(Guid Id, string Kind, Guid TargetId, string Text, DateTimeOffset CreatedAt, string? Statement);

public sealed record CommentRequest
{
    public string Text { get; init; } = "";
}

public sealed record CommentCreatedResponse(Guid Id);

/// <summary>What a comment is about, for the moderators: the target's text and its name, and whether it is shown.</summary>
public sealed record CommentContextResponse(string Kind, Guid TargetId, string? Text, string? AuthorName, string? OrganisationName, MpResponderResponse? Mp, bool Shown);

/// <summary>A comment as a moderator sees it (design 8.23.8). Never who reported it.</summary>
public sealed record QueueCommentResponse(
    Guid Id, BillRefResponse Bill, CommentAuthorResponse Author, string Text, DateTimeOffset CreatedAt, string Status, IReadOnlyList<QueueReportResponse> Reports,
    CommentContextResponse Target, DateTimeOffset? HiddenAt, string? HiddenBy, string? HideStatement);

public sealed record CommentModerationResponse(
    IReadOnlyList<QueueCommentResponse> Reported, IReadOnlyList<QueueCommentResponse> Hidden, int ReportedTotal = 0, int HiddenTotal = 0);

public sealed class CommentRequestValidator : AbstractValidator<CommentRequest>
{
    public CommentRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.Text).Cascade(CascadeMode.Stop)
            .Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage(l["Comment.Required"])
            .Must(t => t.Trim().Length <= MemberTextLimits.Ceiling).WithMessage(l["Comment.TooLong", MemberTextLimits.Number(MemberTextLimits.Ceiling)]); // the setting Texts:MaxLength: where it is written
    }
}

/// <summary>Design 8.23: reading and writing comments, and "Sutinku".</summary>
public interface ICommentsApi
{
    /// <summary>
    /// Public: a target's shown comments, <paramref name="limit"/> of them from <paramref name="cursor"/> on (the first page at
    /// least 10; "Rodyti daugiau komentarų" in an island asks for the next <see cref="CommentLimits.Step"/>).
    /// </summary>
    Task<ApiResult<CommentThreadResponse>> GetThreadAsync(string kind, Guid targetId, int? limit = null, string? cursor = null, CancellationToken cancellationToken = default);

    /// <summary>Public: where a shown comment stands, for the page a link to it lands on (design 8.23.6); not found otherwise.</summary>
    Task<ApiResult<CommentPlaceResponse>> GetPlaceAsync(Guid commentId, CancellationToken cancellationToken = default);

    Task<ApiResult<MyCommentsResponse>> GetMineAsync(string number, CancellationToken cancellationToken = default);

    Task<ApiResult<CommentCreatedResponse>> WriteAsync(string kind, Guid targetId, CommentRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The author edits their comment while nobody has said "Sutinku" (design 8.23.3, owner 2026-11-17).</summary>
    Task<ApiResult> EditAsync(Guid id, CommentRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> AgreeAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ApiResult> RemoveAgreementAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>Moderators: reported and hidden comments, the bill page's answer, and the actions (design 8.23.8).</summary>
public interface ICommentModerationApi
{
    Task<ApiResult<CommentModerationResponse>> GetAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<IReadOnlyList<QueueCommentResponse>>> GetOnBillAsync(string number, CancellationToken cancellationToken = default);

    Task<ApiResult> HideAsync(Guid id, StatementRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> RestoreAsync(Guid id, StatementRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> KeepAsync(Guid id, ModerationRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<AuthorViewResponse>> GetWriterAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ApiResult<RestrictionCreatedResponse>> RestrictWriterAsync(Guid id, RestrictionRequest request, CancellationToken cancellationToken = default);
}
