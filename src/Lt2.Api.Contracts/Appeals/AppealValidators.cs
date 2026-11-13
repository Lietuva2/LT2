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
            .Must(t => t.Trim().Length <= AppealLimits.ReasonMaxLength).WithMessage(l["Reason.TooLong"]);

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

public sealed class OpenAppealRequestValidator : AbstractValidator<OpenAppealRequest>
{
    public OpenAppealRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.Description).Description(l);
        RuleFor(x => x.Threshold).GreaterThan(0).WithMessage(l["Threshold.Positive"]);
    }
}

public sealed class EditAppealRequestValidator : AbstractValidator<EditAppealRequest>
{
    public EditAppealRequestValidator(IStringLocalizer<ValidationMessages> l) =>
        RuleFor(x => x.Description).Description(l);
}

public sealed class ThresholdRequestValidator : AbstractValidator<ThresholdRequest>
{
    public ThresholdRequestValidator(IStringLocalizer<ValidationMessages> l) =>
        RuleFor(x => x.Threshold).GreaterThan(0).WithMessage(l["Threshold.Positive"]);
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
        RuleFor(x => x.Ground).Must(g => AppealLimits.ReportGrounds.Contains(g)).WithMessage(l["Report.Ground"]);
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
        RuleFor(x => x.Link).Must(link => link.Length <= 500 && Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            .WithMessage(l["Flag.Link"]);
        RuleFor(x => x.Note).Must(n => n is null || n.Trim().Length <= 1000).WithMessage(l["Report.TooLong"]);
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