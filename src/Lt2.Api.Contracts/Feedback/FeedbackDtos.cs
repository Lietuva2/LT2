using FluentValidation;
using Microsoft.Extensions.Localization;

namespace Lt2.Api.Contracts.Feedback;

// Design 8.10: feedback from members to the team. Kinds: bug | idea | unclear | other. Statuses: new | seen | done.

/// <summary>What a member sends. Unless <c>MayReply</c>, the item is stored with no link to the account at all.</summary>
public sealed record FeedbackRequest
{
    public string Text { get; init; } = "";

    public string? Kind { get; init; }

    /// <summary>The page the member was on, path only; left out if they removed it.</summary>
    public string? PagePath { get; init; }

    /// <summary>The browser's name and version, for example "Edge 131"; left out if they removed it.</summary>
    public string? Browser { get; init; }

    public bool MayReply { get; init; }
}

public sealed record FeedbackReplyResponse(DateTimeOffset At, string Text);

/// <summary>An item for staff. <c>DisplayName</c> only for linked items; an unlinked item has no author and no reply.</summary>
public sealed record FeedbackItemResponse(
    Guid Id, string? Kind, string Text, string Status, string? Note, string? PagePath, string? Browser, DateOnly CreatedOn, bool Linked, string? DisplayName, IReadOnlyList<FeedbackReplyResponse> Replies);

public sealed record FeedbackUpdateRequest
{
    public string Status { get; init; } = FeedbackStatuses.New;

    /// <summary>The team's internal note, for example the address of a GitHub issue.</summary>
    public string? Note { get; init; }
}

public sealed record FeedbackReplyRequest
{
    public string Text { get; init; } = "";
}

public static class FeedbackLimits
{
    public const int TextMaxLength = 2000;

    public static readonly IReadOnlyList<string> Kinds = FeedbackKinds.All;

    public static readonly IReadOnlyList<string> Statuses = FeedbackStatuses.All;
}

public static class FeedbackRoutes
{
    public const string Send = ApiRoutes.Prefix + "/feedback";
    public const string Staff = ApiRoutes.Prefix + "/moderation/feedback";
    public const string Item = ApiRoutes.Prefix + "/moderation/feedback/{id:guid}";
    public const string Reply = ApiRoutes.Prefix + "/moderation/feedback/{id:guid}/reply";

    public static string ItemOf(Guid id) => $"{Staff}/{id}";

    public static string ReplyOf(Guid id) => $"{Staff}/{id}/reply";
}

/// <summary>Design 8.10: send (a member), and the staff list, status and note, and reply.</summary>
public interface IFeedbackApi
{
    Task<ApiResult> SendAsync(FeedbackRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<IReadOnlyList<FeedbackItemResponse>>> ListAsync(string? kind = null, string? status = null, CancellationToken cancellationToken = default);

    Task<ApiResult> UpdateAsync(Guid id, FeedbackUpdateRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> ReplyAsync(Guid id, FeedbackReplyRequest request, CancellationToken cancellationToken = default);
}

public sealed class FeedbackRequestValidator : AbstractValidator<FeedbackRequest>
{
    public FeedbackRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.Text).Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage(l["Feedback.Empty"])
            .Must(t => (t ?? "").Trim().Length <= FeedbackLimits.TextMaxLength).WithMessage(l["Feedback.TooLong"]);
        RuleFor(x => x.Kind).Must(k => k is null || FeedbackLimits.Kinds.Contains(k)).WithMessage(l["Feedback.Kind"]);
    }
}

public sealed class FeedbackReplyRequestValidator : AbstractValidator<FeedbackReplyRequest>
{
    public FeedbackReplyRequestValidator(IStringLocalizer<ValidationMessages> l) =>
        RuleFor(x => x.Text).Must(t => !string.IsNullOrWhiteSpace(t) && t.Trim().Length <= FeedbackLimits.TextMaxLength).WithMessage(l["Feedback.Empty"]);
}
