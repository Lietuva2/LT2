using Lt2.Api.Contracts.Appeals;

namespace Lt2.Web.Client.Appeals;

/// <summary>
/// The tabs of the moderators' page (design 8.3, owner 2026-11-25): the tab is the page's <c>?skiltis=</c>. First the work
/// lists, in this order, then the reference lists. Without a tab asked for (or with one that does not exist) the page opens
/// on the first work tab with something waiting, or on Projektai when none has; never on a reference tab.
/// </summary>
public static class ModerationTabs
{
    /// <summary>The page's address and its query parameter.</summary>
    public const string Path = "/moderavimas";

    public const string Parameter = "skiltis";

    public const string Records = "projektai";
    public const string Reasons = "argumentai";
    public const string Organisations = "organizacijos";
    public const string Statements = "pozicijos";
    public const string Responses = "atsakymai";
    public const string Profiles = "profiliai";
    public const string Comments = "komentarai";

    public const string Undescribed = "be-aprasymo";
    public const string Hidden = "paslepti";
    public const string Restrictions = "apribojimai";

    public static readonly IReadOnlyList<string> Work = [Records, Reasons, Organisations, Statements, Responses, Profiles, Comments];

    public static readonly IReadOnlyList<string> Reference = [Undescribed, Hidden, Restrictions];

    /// <summary>The address of a tab.</summary>
    public static string Link(string tab) => Path + "?" + Parameter + "=" + Uri.EscapeDataString(tab);

    /// <summary>How many items the tab's list holds.</summary>
    public static int Count(ModerationCountsResponse counts, string tab)
    {
        ArgumentNullException.ThrowIfNull(counts);
        return tab switch
        {
            Records => counts.Records,
            Reasons => counts.Reasons,
            Organisations => counts.Organisations,
            Statements => counts.Statements,
            Responses => counts.Responses,
            Profiles => counts.Profiles,
            Comments => counts.Comments,
            Undescribed => counts.Undescribed,
            Hidden => counts.Hidden,
            Restrictions => counts.Restrictions,
            _ => 0,
        };
    }

    /// <summary>
    /// The tab the page opens on: the one asked for when it exists; else the first work tab with something waiting, or
    /// Projektai when none has (or the counts are not known).
    /// </summary>
    public static string Open(string? asked, ModerationCountsResponse? counts)
    {
        if (asked is not null && (Work.Contains(asked) || Reference.Contains(asked)))
        {
            return asked;
        }

        return counts is null ? Records : Work.FirstOrDefault(tab => Count(counts, tab) > 0) ?? Records;
    }
}
