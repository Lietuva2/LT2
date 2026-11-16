namespace Lt2.Web.Client.Components;

/// <summary>
/// Design 8.21: which menu item a page belongs to, and which of the three tabs under "Projektai" it is. The menu item
/// "Projektai" opens <c>/darbotvarke</c> and is current on the three pages and on everything under them (the history of votes, a bill and its
/// history, a vote, a summary's date page, the introduction letter's public page); "Seimo nariai" on the list, a profile and
/// its votes, a faction and a committee. Paths are site paths with or without the leading <c>/</c>, query and fragment ignored.
/// </summary>
public static class MenuSections
{
    /// <summary>
    /// The three tabs, in their order: the address and the resource key of each. The history of votes is no tab (the owner,
    /// 2026-11-16): it is reached by the link under the agenda's voted list and leads back by a breadcrumb.
    /// </summary>
    public static readonly IReadOnlyList<(ProjectTab Tab, string Path, string Key)> Tabs =
    [
        (ProjectTab.Agenda, "/darbotvarke", "Tabs.Agenda"),
        (ProjectTab.Bills, "/projektai", "Tabs.Bills"),
        (ProjectTab.Digests, "/suvestines", "Tabs.Digests"),
    ];

    /// <summary>Where the menu item "Projektai" leads (design 8.21, the designer's proposal: what is coming first).</summary>
    public const string ProjectsHome = "/darbotvarke";

    /// <summary>The page is one of the three tabs, under one of them, or the history of votes.</summary>
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

/// <summary>The three tabs under "Projektai" (design 8.21).</summary>
public enum ProjectTab
{
    Agenda,
    Bills,
    Digests,
}
