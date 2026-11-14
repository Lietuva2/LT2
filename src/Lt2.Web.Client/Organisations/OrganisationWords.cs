using Lt2.Api.Contracts.Organisations;

namespace Lt2.Web.Client.Organisations;

/// <summary>How organisations are worded on the pages (design 8.14.14); the texts are in AppealUi.resx under <c>Org.</c>.</summary>
public static class OrganisationWords
{
    /// <summary>"Nepatvirtinta", "Patvirtinta per el. pašto adresą @domenas" or "Patvirtino moderatorius".</summary>
    public static string LabelKey(OrganisationLabelResponse label) => label?.ConfirmedBy switch
    {
        "domain" => "Org.Label.domain",
        "moderator" => "Org.Label.moderator",
        _ => "Org.Label.none",
    };

    /// <summary>
    /// The genitive after a number (Lithuanian): singular after a number ending in 1 but not 11 ("21 dalyvio"), plural
    /// otherwise ("1 000 dalyvių", "11 dalyvių"). The goal line uses it: "Siekiame {N} dalyvių".
    /// </summary>
    public static bool GenitiveSingular(int n) => Math.Abs(n) % 10 == 1 && Math.Abs(n) % 100 != 11;

    public static string GoalKey(int goal) => GenitiveSingular(goal) ? "Org.Goal.One" : "Org.Goal.Many";

    /// <summary>The page of an organisation's profile.</summary>
    public static string ProfilePath(string slug) => "/o/" + Uri.EscapeDataString(slug);

    /// <summary>What a user's link points to is shown, never fetched; it gets the attributes the site gives user links.</summary>
    public const string UserLinkRel = "nofollow noopener ugc";
}
