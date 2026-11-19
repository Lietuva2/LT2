using FluentValidation;
using Microsoft.Extensions.Localization;

namespace Lt2.Api.Contracts.Appeals;

internal static class AppealValidation
{
    public static IRuleBuilderOptions<T, string> Side<T>(this IRuleBuilderInitial<T, string> rule, IStringLocalizer l) =>
        rule.Must(s => AppealLimits.Sides.Contains(s)).WithMessage(l["Side.Invalid"]);

    public static IRuleBuilderOptions<T, string> ReasonText<T>(this IRuleBuilderInitial<T, string> rule, IStringLocalizer l) =>
        rule.Cascade(CascadeMode.Stop)
            .Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage(l["Reason.Required"])
            // Only the technical ceiling here; the setting Texts:MaxLength is checked where the argument is written
            // (design 6.8, 8.2), and its refusal names the number that holds.
            .Must(t => t.Trim().Length <= MemberTextLimits.Ceiling).WithMessage(l["Reason.TooLong", MemberTextLimits.Number(MemberTextLimits.Ceiling)]);

    public static IRuleBuilderOptions<T, string> Description<T>(this IRuleBuilderInitial<T, string> rule, IStringLocalizer l) =>
        rule.Cascade(CascadeMode.Stop)
            .Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage(l["Description.Required"])
            .Must(t => t.Trim().Length <= AppealLimits.DescriptionMaxLength).WithMessage(l["Description.TooLong"]);

    public static IRuleBuilderOptions<T, string?> Statement<T>(this IRuleBuilderInitial<T, string?> rule, IStringLocalizer l, bool required) =>
        required
            ? rule.Cascade(CascadeMode.Stop)
                .Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage(l["Statement.Required"])
                .Must(t => t!.Trim().Length <= AppealLimits.StatementMaxLength).WithMessage(l["Statement.TooLong"])
            : rule.Must(t => t is null || t.Trim().Length <= AppealLimits.StatementMaxLength).WithMessage(l["Statement.TooLong"]);
}

/// <summary>The pasted official text for an AI draft (design 8.11.4): required, at most 20 000 characters, never cut.</summary>
public sealed class DescriptionDraftRequestValidator : AbstractValidator<DescriptionDraftRequest>
{
    public const int SourceMaxLength = AppealLimits.DraftSourceMaxLength;

    public DescriptionDraftRequestValidator(IStringLocalizer<ValidationMessages> l) =>
        RuleFor(x => x.SourceText).Cascade(CascadeMode.Stop)
            .Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage(l["Draft.SourceRequired"])
            .Must(t => t.Length <= SourceMaxLength).WithMessage(l["Draft.SourceTooLong"]);
}

public sealed class PositionRequestValidator : AbstractValidator<PositionRequest>
{
    public PositionRequestValidator(IStringLocalizer<ValidationMessages> l) => RuleFor(x => x.Side).Side(l);
}

public sealed class ReasonRequestValidator : AbstractValidator<ReasonRequest>
{
    public ReasonRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.Side).Side(l);
        RuleFor(x => x.Text).ReasonText(l);
    }
}

public sealed class ReportRequestValidator : AbstractValidator<ReportRequest>
{
    public ReportRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.TargetKind).Must(k => AppealLimits.ReportTargets.Contains(k)).WithMessage(l["Report.Target"]);
        RuleFor(x => x.TargetId).NotEmpty().WithMessage(l["Report.Target"]);
        RuleFor(x => x.Ground).Must((x, g) => AppealLimits.ReportGroundsFor(x.TargetKind).Contains(g)).WithMessage(l["Report.Ground"]);
        RuleFor(x => x.Text).Must(t => t is null || t.Trim().Length <= AppealLimits.ReportTextMaxLength).WithMessage(l["Report.TooLong"]);
    }
}

public sealed class ModerationRequestValidator : AbstractValidator<ModerationRequest>
{
    public ModerationRequestValidator(IStringLocalizer<ValidationMessages> l) => RuleFor(x => x.Statement).Statement(l, required: false);
}

/// <summary>Returning or hiding needs a statement of reasons (design 8.3).</summary>
public sealed class RestrictionRequestValidator : AbstractValidator<RestrictionRequest>
{
    public RestrictionRequestValidator(IStringLocalizer<ValidationMessages> l) =>
        RuleFor(x => (string?)x.Statement).Statement(l, required: true).OverridePropertyName("statement");
}

public sealed class StatementRequestValidator : AbstractValidator<StatementRequest>
{
    public StatementRequestValidator(IStringLocalizer<ValidationMessages> l) =>
        RuleFor(x => (string?)x.Statement).Statement(l, required: true).OverridePropertyName("statement");
}

public sealed class ModeratorThresholdRequestValidator : AbstractValidator<ModeratorThresholdRequest>
{
    public ModeratorThresholdRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.Threshold).GreaterThan(0).WithMessage(l["Threshold.Positive"]);
        RuleFor(x => (string?)x.Statement).Statement(l, required: true).OverridePropertyName("statement");
    }
}

public sealed class ModeratorDescriptionRequestValidator : AbstractValidator<ModeratorDescriptionRequest>
{
    public ModeratorDescriptionRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.Description).Description(l);
        RuleFor(x => (string?)x.Statement).Statement(l, required: true).OverridePropertyName("statement");

        // Design 8.13 (decided 2026-11-14): empty, or https on lrs.lt or under it, no user info, at most 500 characters.
        RuleFor(x => x.DescriptionUrl).Must(OfficialTextAddress.IsAcceptable).WithMessage(l["DescriptionUrl.Invalid"]).OverridePropertyName("descriptionUrl");
    }
}

/// <summary>
/// A moderator's close of a record (design 8.1 step 6): the reason, the day, the source (required, the official text's rule) and
/// the statement. Whether the day is in the future is checked where it is stored, against the Vilnius day of the server's clock.
/// </summary>
public sealed class CloseRecordRequestValidator : AbstractValidator<CloseRecordRequest>
{
    public CloseRecordRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.Reason).Must(r => ClosingReasons.ByModerator.Contains(r)).WithMessage(l["Closing.Reason"]).OverridePropertyName("reason");
        RuleFor(x => x.DecidedOn).NotEqual(default(DateOnly)).WithMessage(l["Closing.Date"]).OverridePropertyName("decidedOn");
        RuleFor(x => x.SourceUrl)
            .Must(u => OfficialTextAddress.Normalise(u) is not null && OfficialTextAddress.IsAcceptable(u))
            .WithMessage(l["Closing.Url"])
            .OverridePropertyName("sourceUrl");
        RuleFor(x => (string?)x.Statement).Statement(l, required: true).OverridePropertyName("statement");
    }
}

public sealed class MergeReasonRequestValidator : AbstractValidator<MergeReasonRequest>
{
    public MergeReasonRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.IntoReasonId).NotEmpty().WithMessage(l["Merge.Target"]);
        RuleFor(x => (string?)x.Statement).Statement(l, required: true).OverridePropertyName("statement");
    }
}

public sealed class FlagRequestValidator : AbstractValidator<FlagRequest>
{
    public FlagRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.Kind).Must(k => k is "date-wrong" or "sooner").WithMessage(l["Flag.Kind"]);
        RuleFor(x => x.Link).Must(link => link.Length <= AppealLimits.FlagLinkMaxLength && Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            .WithMessage(l["Flag.Link"]);
        RuleFor(x => x.Note).Must(n => n is null || n.Trim().Length <= AppealLimits.FlagNoteMaxLength).WithMessage(l["Report.TooLong"]);
    }
}

public sealed class ModeratorPlannedRequestValidator : AbstractValidator<ModeratorPlannedRequest>
{
    public ModeratorPlannedRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.Date).NotEqual(default(DateOnly)).WithMessage(l["Planned.Date"]);
        RuleFor(x => x.Stage).Must(s => s is null or "presentation" or "consideration" or "adoption").WithMessage(l["Planned.Stage"]);
        RuleFor(x => (string?)x.Statement).Statement(l, required: true).OverridePropertyName("statement");
    }
}

public sealed class UrgentDigestRequestValidator : AbstractValidator<Digests.UrgentDigestRequest>
{
    public UrgentDigestRequestValidator(IStringLocalizer<ValidationMessages> l) =>
        RuleFor(x => (string?)x.Statement).Statement(l, required: true).OverridePropertyName("statement");
}