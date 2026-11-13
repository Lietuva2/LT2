using System.Globalization;
using Microsoft.Extensions.Localization;

namespace Lt2.Web.Components.Parliament;

/// <summary>Marker type for the Lithuanian texts of the parliament pages in ParliamentText.resx.</summary>
public sealed class ParliamentText;

/// <summary>
/// How the parliament pages write numbers, shares, dates and codes: tabular figures with a no-break space for
/// thousands, whole per cent, Lithuanian dates in Vilnius time. A share with nothing to count is "no data", never 0 %.
/// </summary>
internal sealed class Formats(IStringLocalizer<ParliamentText> t)
{
    private const char NoBreak = ' ';
    private static readonly TimeZoneInfo Vilnius = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vilnius");
    private static readonly NumberFormatInfo Numbers = new() { NumberGroupSeparator = NoBreak.ToString(), NumberGroupSizes = [3] };

    public IStringLocalizer<ParliamentText> T => t;

    public static string Number(int? value) => value is { } n ? n.ToString("#,0", Numbers) : "–";

    /// <summary>Whole per cent, rounded half away from zero; null when there is nothing to count.</summary>
    public static string? Percent(decimal? share) =>
        share is { } s ? Number((int)Math.Round(s * 100, MidpointRounding.AwayFromZero)) + NoBreak + "%" : null;

    public string Date(DateOnly date) => string.Format(CultureInfo.InvariantCulture, t["Date.Format"], date.Year, t[$"Month.{date.Month}"], date.Day);

    public string Date(DateTimeOffset at) => Date(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, Vilnius).DateTime));

    /// <summary>A day in numbers, yyyy-MM-dd (<c>AGENTS.md</c> "Wording": dates shown as numbers).</summary>
    public static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>A moment in numbers, yyyy-MM-dd HH:mm, Vilnius time: for lists such as the description's history.</summary>
    public static string Stamp(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, Vilnius).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public string Time(DateTimeOffset at) =>
        string.Format(CultureInfo.InvariantCulture, t["Time.Format"], Date(at), TimeZoneInfo.ConvertTime(at, Vilnius).ToString("HH:mm", CultureInfo.InvariantCulture));

    public string Choice(string? code) => code is null ? t["Consensus.Tag"] : t[$"Choice.{code}"];

    public static string ChoiceClass(string? code) => code switch
    {
        "for" => "choice choice--for",
        "against" => "choice choice--against",
        "did-not-vote" => "choice choice--absent",
        _ => "choice",
    };

    public string Stage(string? code) => code is null ? "" : t[$"Stage.{code}"];

    public string Kind(string code) => t[$"VoteKind.{code}"];

    public string Position(string code) => t[$"Position.{code}"];

    public string Counts(int @for, int against, int abstain) =>
        string.Format(CultureInfo.InvariantCulture, t["Faction.Counts"], Number(@for), Number(against), Number(abstain));

    public string Format(string key, params object?[] values) => string.Format(CultureInfo.InvariantCulture, t[key], values);
}
