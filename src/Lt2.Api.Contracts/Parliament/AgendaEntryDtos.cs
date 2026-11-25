using FluentValidation;
using Microsoft.Extensions.Localization;

namespace Lt2.Api.Contracts.Parliament;

/// <summary>
/// Design 8.4, "Entering a day's agenda": the day's bills, one line each, <c>number | stage | title</c>
/// (<c>XVP-1980 | antrasis svarstymas | 2027–2029 metų biudžeto patvirtinimo įstatymo projektas</c>); blank lines are ignored.
/// <c>SourceUrl</c> is the day's agenda document (https, on <c>e-seimas.lrs.lt</c> or <c>www.lrs.lt</c>). The day goes in the
/// address of <c>PUT /moderation/agenda/{day}</c>.
/// </summary>
public sealed record AgendaEntryRequest
{
    public string SourceUrl { get; init; } = "";

    public string Lines { get; init; } = "";
}

/// <summary><c>POST /moderation/agenda/preview</c>: the same, with the sitting day (from today up to 31 days ahead).</summary>
public sealed record AgendaEntryPreviewRequest
{
    public DateOnly Day { get; init; }

    public string SourceUrl { get; init; } = "";

    public string Lines { get; init; } = "";
}

/// <summary>
/// One bill of the day: its line, number (revision dropped), stage code (<see cref="BillStages"/>, null when the line names
/// none), the title as written, <c>Created</c> when the site does not know it (a preview: saving creates it; a save: it did),
/// and what saving does to it (<see cref="AgendaEntryChanges"/>).
/// </summary>
public sealed record AgendaEntryBillResponse(int Line, string Number, string? Stage, string Title, bool Created, string Change);

/// <summary>A refused line: its 1-based number and why (<see cref="AgendaEntryRefusals"/>).</summary>
public sealed record AgendaEntryRefusalResponse(int Line, string Reason);

/// <summary>
/// What saving the day does (a preview, <c>Saved</c> false) or did: the counts, each bill, the numbers taken off the day, and the
/// refused lines (only a preview has any: a save with one is refused, <c>agenda-entry.lines</c>).
/// </summary>
public sealed record AgendaEntryResponse(
    DateOnly Day,
    string SourceUrl,
    bool Saved,
    int Added,
    int StageChanged,
    int Unchanged,
    int Removed,
    int Created,
    IReadOnlyList<AgendaEntryBillResponse> Bills,
    IReadOnlyList<string> RemovedNumbers,
    IReadOnlyList<AgendaEntryRefusalResponse> Refused);

/// <summary>A day from today with entered rows: how many bills, when it was last saved and its document (null if never saved).</summary>
public sealed record AgendaEntryDayResponse(DateOnly Day, int Bills, DateTimeOffset? LastSaved, string? SourceUrl);

/// <summary><c>GET /moderation/agenda</c>: the entered days from today, earliest first.</summary>
public sealed record AgendaEntryDaysResponse(IReadOnlyList<AgendaEntryDayResponse> Days);

/// <summary>The limits the API states for an entered day; the Parliament module checks the same, with the day and the host.</summary>
public static class AgendaEntryLimits
{
    /// <summary>The longest text the lines may be sent as.</summary>
    public const int LinesMaxLength = 100_000;

    /// <summary>The longest document address.</summary>
    public const int SourceMaxLength = 2000;
}

public sealed class AgendaEntryRequestValidator : AbstractValidator<AgendaEntryRequest>
{
    public AgendaEntryRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.SourceUrl).AgendaSource(l);
        RuleFor(x => x.Lines).AgendaLines(l);
    }
}

public sealed class AgendaEntryPreviewRequestValidator : AbstractValidator<AgendaEntryPreviewRequest>
{
    public AgendaEntryPreviewRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.Day).NotEqual(default(DateOnly)).WithMessage(l["AgendaEntry.Day"]).OverridePropertyName("day");
        RuleFor(x => x.SourceUrl).AgendaSource(l);
        RuleFor(x => x.Lines).AgendaLines(l);
    }
}

internal static class AgendaEntryValidation
{
    public static IRuleBuilderOptions<T, string> AgendaSource<T>(this IRuleBuilderInitial<T, string> rule, IStringLocalizer l) =>
        rule.Cascade(CascadeMode.Stop)
            .Must(s => !string.IsNullOrWhiteSpace(s)).WithMessage(l["AgendaEntry.Source"])
            .Must(s => s.Trim().Length <= AgendaEntryLimits.SourceMaxLength).WithMessage(l["AgendaEntry.Source"])
            .OverridePropertyName("sourceUrl");

    public static IRuleBuilderOptions<T, string> AgendaLines<T>(this IRuleBuilderInitial<T, string> rule, IStringLocalizer l) =>
        rule.Must(s => (s ?? "").Length <= AgendaEntryLimits.LinesMaxLength).WithMessage(l["AgendaEntry.TooLong"])
            .OverridePropertyName("lines");
}
