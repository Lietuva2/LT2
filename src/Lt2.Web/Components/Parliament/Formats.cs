using Lt2.Web.Client;
using Microsoft.Extensions.Localization;

namespace Lt2.Web.Components.Parliament;

/// <summary>Marker type for the Lithuanian texts of the parliament pages in ParliamentText.resx.</summary>
public sealed class ParliamentText;

/// <summary>
/// The parliament pages' words for dates, choices, stages and positions, in Lithuanian. Numbers and dates in numbers
/// come from <see cref="Display"/>, the one formatting class of the interface; a share with nothing to count is
/// "no data", never 0 %.
/// </summary>
internal sealed class Formats(IStringLocalizer<ParliamentText> t)
{
    public IStringLocalizer<ParliamentText> T => t;

    public string Date(DateOnly date) => t.Format("Date.Format", date.Year, t[$"Month.{date.Month}"], date.Day);

    public string Date(DateTimeOffset at) => Date(Display.DayOf(at));

    public string Time(DateTimeOffset at) => t.Format("Time.Format", Date(at), Display.Time(at));

    public string Choice(string? code) => code is null ? t["Consensus.Tag"] : t[$"Choice.{code}"];

    /// <summary>The mark of an MP's choice or a faction's position: for, against, or the hollow mark for no vote.</summary>
    public static string ChoiceClass(string? code) => code switch
    {
        "for" => "choice choice--for",
        "against" => "choice choice--against",
        "did-not-vote" or "free" or "nobody-voted" => "choice choice--absent",
        _ => "choice",
    };

    public string Stage(string? code) => code is null ? "" : t[$"Stage.{code}"];

    public string Kind(string code) => t[$"VoteKind.{code}"];

    public string Position(string code) => t[$"Position.{code}"];

    public string Counts(int @for, int against, int abstain) =>
        t.Format("Faction.Counts", Display.Number(@for), Display.Number(against), Display.Number(abstain));
}
