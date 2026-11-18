using Lt2.SharedKernel;
using Lt2.Web.Client;
using Microsoft.Extensions.Localization;

namespace Lt2.Web.Components.Agenda;

/// <summary>The agenda's counts with their nouns in Lithuanian ("1 projektas", "3 projektai", "40 projektų").</summary>
internal static class AgendaWords
{
    /// <summary>"40 projektų": the bills planned for a day.</summary>
    public static string Bills(IStringLocalizer<PageText> t, int n) => Counted(t, "Agenda.Bills", n);

    /// <summary>"26 balsavimai": the stage votes of a day.</summary>
    public static string Votes(IStringLocalizer<PageText> t, int n) => Counted(t, "Agenda.Votes", n);

    /// <summary>"Dar 12 projektų, kurių atviruosiuose Seimo duomenyse dar nėra" (design 8.11.1).</summary>
    public static string Unknown(IStringLocalizer<PageText> t, int n) => Counted(t, "Agenda.Unknown", n);

    private static string Counted(IStringLocalizer<PageText> t, string key, int n)
    {
        ArgumentNullException.ThrowIfNull(t);
        return t[LithuanianCounts.Pick(n, key + ".One", key + ".Few", key + ".Many"), Display.Number(n)];
    }
}
