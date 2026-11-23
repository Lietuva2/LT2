namespace Lt2.Web.Client.Components;

/// <summary>
/// Design 8.21: which menu item a page belongs to, and which of the four tabs under "Seimas" it is. The menu item
/// "Seimas" opens a dropdown with the four tabs "Projektai", "Balsavimai", "Suvestinės" and "Seimo nariai" and is current on the tab
/// pages and on everything under them (the agenda, the bills and their pages, the votes, a summary's date page, the
/// introduction letter's public page, an MP's profile, a faction, a committee); "Statistika" is a menu item of its own (design
/// 8.26). "Darbotvarkė", "Aktyvūs" and "Uždaryti" are views of the tab "Projektai", not tabs. Paths are site paths with or
/// without the leading <c>/</c>, query and fragment ignored.
/// </summary>
public static class MenuSections
{
    /// <summary>
    /// Design 8.28.2: the one list of every item of the menu and the footer, in the order they are shown: its address, its
    /// resource keys (the name <c>Menu.{Slug}</c>, the line that says what the page is <c>Menu.{Slug}.Line</c>, both in
    /// <c>UiText</c>), and where it stands. The menu, the dropdown of "Seimas", the tab row, the footer and "Kur ką rasti"
    /// on <c>/metodika</c> are all drawn from it; <c>MenuTests</c> fails for an item without a name or a line. Element ids
    /// are <c>nav-{Slug}</c> (menu and dropdown), <c>{Slug}</c> (the tab row: only the tabs), <c>foot-{Slug}</c> and
    /// <c>where-{Slug}</c>.
    /// </summary>
    public static readonly IReadOnlyList<MenuItem> Items =
    [
        new("tab-projektai", SeimasHome, "Bills", MenuPlace.Seimas, SeimasTab.Projects),
        new("tab-seimas-balsavimai", VoteHistoryPath, "Votes", MenuPlace.Seimas, SeimasTab.Votes),
        new("tab-suvestines", "/suvestines", "Digests", MenuPlace.Seimas, SeimasTab.Digests),
        new("tab-mps", Mps, "Mps", MenuPlace.Seimas, SeimasTab.Mps),
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
    /// The four tabs, in their order: the dropdown of the menu item "Seimas" and the row under the heading "Seimas" on the
    /// tab pages. The first, "Projektai", links to the agenda, its landing view.
    /// </summary>
    public static readonly IReadOnlyList<MenuItem> Tabs = [.. In(MenuPlace.Seimas)];

    /// <summary>The menu's items in the menu's order, the account item left out: the footer's group "Naršyti" (design 8.21).</summary>
    public static readonly IReadOnlyList<MenuItem> Browse = [.. In(MenuPlace.Seimas), .. In(MenuPlace.Menu)];

    /// <summary>The first tab's landing view: what is coming (design 8.21, the designer's proposal: the most useful first view).</summary>
    public const string SeimasHome = "/darbotvarke";

    /// <summary>The vote history, the tab "Balsavimai" (the page of one vote, <c>/seimas/balsavimai/{id}</c>, is under it).</summary>
    public const string VoteHistoryPath = "/seimas/balsavimai";

    /// <summary>The list of MPs, the tab "Seimo nariai".</summary>
    public const string Mps = "/seimo-nariai";

    /// <summary>The statistics page, "Statistika" in the main menu (design 8.26).</summary>
    public const string Statistics = "/statistika";

    /// <summary>The page is the statistics page.</summary>
    public static bool InStatistics(string? path) => Normalise(path) == Statistics.TrimStart('/');

    /// <summary>The page is one of the four tabs or under one of them.</summary>
    public static bool InSeimas(string? path)
    {
        var p = Normalise(path);
        return p == "darbotvarke"
            || Under(p, "projektai")
            || Under(p, "seimas")
            || (Under(p, "suvestines") && !Under(p, "suvestines/nustatymai") && !Under(p, "suvestines/kopijos"))
            || Under(p, "kreipimaisi")
            || Under(p, "seimo-nariai")
            || Under(p, "p")
            || Under(p, "frakcijos")
            || Under(p, "komitetai");
    }

    /// <summary>
    /// How an item of the menu's line is marked on a page: <c>page</c> on its own page, nothing elsewhere ("Statistika").
    /// </summary>
    public static string? CurrentMark(MenuItem item, string? path) =>
        string.Equals("/" + Normalise(path), item.Path, StringComparison.Ordinal) ? "page" : null;

    /// <summary>
    /// The tab a page is exactly (not a page under it), or null: "Projektai" on the agenda and the bills' lists (its views),
    /// "Balsavimai" on the vote history, "Suvestinės" on the list, "Seimo nariai" on the list. A vote's own page is under
    /// "Balsavimai" and marks no tab.
    /// </summary>
    public static SeimasTab? TabOf(string? path) => ("/" + Normalise(path)) switch
    {
        SeimasHome or "/projektai" => SeimasTab.Projects,
        VoteHistoryPath => SeimasTab.Votes,
        "/suvestines" => SeimasTab.Digests,
        Mps => SeimasTab.Mps,
        _ => null,
    };

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
/// <param name="Tab">The tab it is, for the four entries of "Seimas".</param>
public sealed record MenuItem(string Slug, string Path, string Key, MenuPlace Place, SeimasTab? Tab = null)
{
    /// <summary>The resource key of its name.</summary>
    public string NameKey => "Menu." + Key;

    /// <summary>The resource key of the one line that says what the page is.</summary>
    public string LineKey => "Menu." + Key + ".Line";
}

/// <summary>Where an item of the menu or the footer stands (design 8.21, 8.28.2).</summary>
public enum MenuPlace
{
    /// <summary>The dropdown of "Seimas" and the tab row.</summary>
    Seimas,

    /// <summary>The menu's one line: "Statistika".</summary>
    Menu,

    /// <summary>The account item (a member's; a visitor has "Prisijungti" there).</summary>
    Account,

    /// <summary>The footer's group "Apie".</summary>
    FooterAbout,

    /// <summary>The footer's group "Dalyvauti".</summary>
    FooterTakePart,
}

/// <summary>The four tabs under "Seimas" (design 8.21).</summary>
public enum SeimasTab
{
    Projects,
    Votes,
    Digests,
    Mps,
}
