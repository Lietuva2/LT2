using Lt2.Api.Contracts.Me;
using Microsoft.Extensions.Localization;

namespace Lt2.Web.Client.Appeals;

/// <summary>
/// The Lithuanian form a noun takes after a number: <see cref="NumberForm.One"/> after 1, 21, 31 … (not 11): "1 naujas
/// argumentas"; <see cref="NumberForm.Few"/> after 2–9, 22–29 … (not 12–19): "3 nauji argumentai"; <see cref="NumberForm.Many"/>
/// after 0, 10–20, 30 …: "10 naujų argumentų".
/// </summary>
public static class NumberForms
{
    public static NumberForm Of(int n)
    {
        var a = Math.Abs(n);
        var last = a % 10;
        var lastTwo = a % 100;
        return last == 1 && lastTwo != 11 ? NumberForm.One
            : last is >= 2 and <= 9 && lastTwo is < 12 or > 19 ? NumberForm.Few
            : NumberForm.Many;
    }
}

public enum NumberForm
{
    One,
    Few,
    Many,
}

/// <summary>
/// What is new on a followed bill as the member's lists say it (design 8.11.8): "3 nauji argumentai · 5 nauji komentarai", the
/// kinds with something new only, in the order arguments, comments, organisations' statements, MPs' responses.
/// </summary>
public static class NewsLine
{
    public static string Of(IStringLocalizer<AppealUi> l, BillNewsCountsResponse counts)
    {
        ArgumentNullException.ThrowIfNull(l);
        ArgumentNullException.ThrowIfNull(counts);
        var parts = new List<string>();
        Add("Reasons", counts.Reasons);
        Add("Comments", counts.Comments);
        Add("Statements", counts.Statements);
        Add("Responses", counts.Responses);
        return string.Join(" · ", parts);

        void Add(string kind, int n)
        {
            if (n > 0)
            {
                parts.Add(l.Format($"News.{kind}.{NumberForms.Of(n)}", Display.Number(n)));
            }
        }
    }
}
