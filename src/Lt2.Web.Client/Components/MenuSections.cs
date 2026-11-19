namespace Lt2.Web.Client.Components;

/// <summary>
/// Design 8.21: which menu item a page belongs to, and which of the four tabs under "Projektai" it is. The menu item
/// "Projektai" opens a dropdown with the four tabs (owner, 2026-11-17; "Balsavimai" back as a tab, owner 2026-11-19) and is
/// current on the four pages and on everything under them (a bill and its history, a vote, a summary's date page, the
/// introduction letter's public page); "Seimo nariai" on the list, a profile and its votes, a faction and a committee; "Statistika" on the statistics page
/// (design 8.26; owner, 2026-11-17: in the main menu, no longer a fourth tab). Paths are site paths with or without the
/// leading <c>/</c>, query and fragment ignored.
/// </summary>
public static class MenuSections
{
    /// <summary>
    /// The four tabs, in their order: the address and the resource key of each. They are the dropdown of the menu item
    /// "Projektai" and the row under the four pages' headings. The history of votes is a tab again (the owner, 2026-11-19:
    /// the agenda grew large and the link under its voted list was too hard to reach); that link stays.
    /// </summary>
    public static readonly IReadOnlyList<(ProjectTab Tab, string Path, string Key)> Tabs =
    [
        (ProjectTab.Agenda, "/darbotvarke", "Tabs.Agenda"),
        (ProjectTab.Bills, "/projektai", "Tabs.Bills"),
        (ProjectTab.Votes, "/seimas/balsavimai", "Tabs.Votes"),
        (ProjectTab.Digests, "/suvestines", "Tabs.Digests"),
    ];

    /// <summary>The first of the tabs: what is coming (design 8.21, the designer's proposal: the most useful first view).</summary>
    public const string ProjectsHome = "/darbotvarke";

    /// <summary>The statistics page, "Statistika" in the main menu (design 8.26; owner, 2026-11-17, was <c>/seimas/analitika</c>).</summary>
    public const string Statistics = "/statistika";

    /// <summary>The page is the statistics page.</summary>
    public static bool InStatistics(string? path) => Normalise(path) == Statistics.TrimStart('/');

    /// <summary>The page is one of the four tabs or under one of them.</summary>
    public static bool InProjects(string? path)
    {
        var p = Normalise(path);
        return p == "darbotvarke"
            || Under(p, "projektai")
            || Under(p, "seimas")
            || (Under(p, "suvestines") && !Under(p, "suvestines/nustatymai") && !Under(p, "suvestines/kopijos"))
            || Under(p, "kreipimaisi");
    }

    /// <summary>The page belongs to "Seimo nariai": the list, a profile (and its votes), a faction, a committee.</summary>
    public static bool InMps(string? path)
    {
        var p = Normalise(path);
        return Under(p, "seimo-nariai") || Under(p, "p") || Under(p, "frakcijos") || Under(p, "komitetai");
    }

    /// <summary>The tab a page is exactly (not a page under it), or null.</summary>
    public static ProjectTab? TabOf(string? path)
    {
        var p = "/" + Normalise(path);
        foreach (var (tab, tabPath, _) in Tabs)
        {
            if (string.Equals(p, tabPath, StringComparison.Ordinal))
            {
                return tab;
            }
        }

        return null;
    }

    private static bool Under(string path, string section) =>
        path == section || path.StartsWith(section + "/", StringComparison.Ordinal);

    private static string Normalise(string? path)
    {
        var p = (path ?? "").Split('?', '#')[0].Trim('/');
        return p.ToLowerInvariant();
    }
}

/// <summary>The four tabs under "Projektai" (design 8.21).</summary>
public enum ProjectTab
{
    Agenda,
    Bills,
    Votes,
    Digests,
}
