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
    /// Design 8.28.2: the one list of every item of the menu and the footer, in the order they are shown: its address, its
    /// resource keys (the name <c>Menu.{Slug}</c>, the line that says what the page is <c>Menu.{Slug}.Line</c>, both in
    /// <c>UiText</c>), and where it stands. The menu, the dropdown of "Projektai", the tab row, the footer and "Kur ką rasti"
    /// on <c>/metodika</c> are all drawn from it; <c>MenuTests</c> fails for an item without a name or a line. Element ids
    /// are <c>nav-{Slug}</c> (menu and dropdown), <c>{Slug}</c> (the tab row: only the tabs), <c>foot-{Slug}</c> and
    /// <c>where-{Slug}</c>.
    /// </summary>
    public static readonly IReadOnlyList<MenuItem> Items =
    [
        new("tab-darbotvarke", "/darbotvarke", "Agenda", MenuPlace.Projects, ProjectTab.Agenda),
        new("tab-projektai", "/projektai", "Bills", MenuPlace.Projects, ProjectTab.Bills),
        new("tab-seimas-balsavimai", "/seimas/balsavimai", "Votes", MenuPlace.Projects, ProjectTab.Votes),
        new("tab-suvestines", "/suvestines", "Digests", MenuPlace.Projects, ProjectTab.Digests),
        new("mps", Mps, "Mps", MenuPlace.Menu),
        new("statistics", Statistics, "Statistics", MenuPlace.Menu),
        new("account", AccountScreens.Profile, "Account", MenuPlace.Account),
        new("methodology", "/metodika", "Methodology", MenuPlace.FooterAbout),
        new("moderation-rules", "/moderavimo-taisykles", "ModerationRules", MenuPlace.FooterAbout),
        new("terms", "/taisykles", "Terms", MenuPlace.FooterAbout),
        new("privacy", "/privatumas", "Privacy", MenuPlace.FooterAbout),
        new("organisations", "/organizacijos", "Organisations", MenuPlace.FooterTakePart),
        new("feedback", "/atsiliepimai", "Feedback", MenuPlace.FooterTakePart),
    ];

    /// <summary>The items standing in one place, in their order.</summary>
    public static IEnumerable<MenuItem> In(MenuPlace place) => Items.Where(i => i.Place == place);

    /// <summary>
    /// The four tabs, in their order: the dropdown of the menu item "Projektai" and the row under the four pages' headings
    /// (owner, 2026-11-19: "Balsavimai" a tab again; the link under the agenda's voted list stays).
    /// </summary>
    public static readonly IReadOnlyList<MenuItem> Tabs = [.. In(MenuPlace.Projects)];

    /// <summary>The menu's items in the menu's order, the account item left out: the footer's group "Naršyti" (design 8.21).</summary>
    public static readonly IReadOnlyList<MenuItem> Browse = [.. In(MenuPlace.Projects), .. In(MenuPlace.Menu)];

    /// <summary>The first of the tabs: what is coming (design 8.21, the designer's proposal: the most useful first view).</summary>
    public const string ProjectsHome = "/darbotvarke";

    /// <summary>The list of MPs, "Seimo nariai" in the main menu.</summary>
    public const string Mps = "/seimo-nariai";

    /// <summary>The statistics page, "Statistika" in the main menu (design 8.26).</summary>
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

    /// <summary>
    /// How an item of the menu's line is marked on a page: <c>page</c> on its own page, <c>true</c> on a page under it
    /// ("Seimo nariai": a profile, a faction, a committee), nothing elsewhere.
    /// </summary>
    public static string? CurrentMark(MenuItem item, string? path)
    {
        if (string.Equals("/" + Normalise(path), item.Path, StringComparison.Ordinal))
        {
            return "page";
        }

        return item.Path == Mps && InMps(path) ? "true" : null;
    }

    /// <summary>The tab a page is exactly (not a page under it), or null.</summary>
    public static ProjectTab? TabOf(string? path)
    {
        var p = "/" + Normalise(path);
        return Tabs.FirstOrDefault(t => string.Equals(p, t.Path, StringComparison.Ordinal))?.Tab;
    }

    private static bool Under(string path, string section) =>
        path == section || path.StartsWith(section + "/", StringComparison.Ordinal);

    private static string Normalise(string? path)
    {
        var p = (path ?? "").Split('?', '#')[0].Trim('/');
        return p.ToLowerInvariant();
    }
}

/// <summary>One item of the menu or the footer (design 8.28.2).</summary>
/// <param name="Slug">The part of its element ids after <c>nav-</c>, <c>foot-</c> or <c>where-</c>.</param>
/// <param name="Path">Its address.</param>
/// <param name="Key">Its resource keys: <c>Menu.{Key}</c> the name, <c>Menu.{Key}.Line</c> what the page is.</param>
/// <param name="Place">Where it stands.</param>
/// <param name="Tab">The tab it is, for the four entries of "Projektai".</param>
public sealed record MenuItem(string Slug, string Path, string Key, MenuPlace Place, ProjectTab? Tab = null)
{
    /// <summary>The resource key of its name.</summary>
    public string NameKey => "Menu." + Key;

    /// <summary>The resource key of the one line that says what the page is.</summary>
    public string LineKey => "Menu." + Key + ".Line";
}

/// <summary>Where an item of the menu or the footer stands (design 8.21, 8.28.2).</summary>
public enum MenuPlace
{
    /// <summary>The dropdown of "Projektai" and the tab row.</summary>
    Projects,

    /// <summary>The menu's one line: "Seimo nariai", "Statistika".</summary>
    Menu,

    /// <summary>The account item (a member's; a visitor has "Prisijungti" there).</summary>
    Account,

    /// <summary>The footer's group "Apie".</summary>
    FooterAbout,

    /// <summary>The footer's group "Dalyvauti".</summary>
    FooterTakePart,
}

/// <summary>The four tabs under "Projektai" (design 8.21).</summary>
public enum ProjectTab
{
    Agenda,
    Bills,
    Votes,
    Digests,
}
