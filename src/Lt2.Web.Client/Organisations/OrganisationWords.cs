using Lt2.Api.Contracts.Organisations;
using Lt2.Web.Client.Appeals;
using Microsoft.Extensions.Localization;

namespace Lt2.Web.Client.Organisations;

/// <summary>How organisations are worded on the pages (design 8.14.14); the texts are in AppealUi.resx under <c>Org.</c>.</summary>
public static class OrganisationWords
{
    /// <summary>What a user's link points to is shown, never fetched; it gets the attributes the site gives user links.</summary>
    public const string UserLinkRel = "nofollow noopener ugc";

    /// <summary>"Nepatvirtinta", "Patvirtinta per el. pašto adresą @domenas" or "Patvirtino moderatorius".</summary>
    public static string LabelKey(OrganisationLabelResponse label) => label?.ConfirmedBy switch
    {
        "domain" => "Org.Label.domain",
        "moderator" => "Org.Label.moderator",
        _ => "Org.Label.none",
    };

    /// <summary>The label's words, with the domain filled in where it was confirmed by email.</summary>
    public static string LabelText(IStringLocalizer<AppealUi> l, OrganisationLabelResponse label)
    {
        ArgumentNullException.ThrowIfNull(l);
        ArgumentNullException.ThrowIfNull(label);
        return l.Format(LabelKey(label), label.Domain ?? "");
    }

    /// <summary>The mark of a stance: for and against in their colours, remarks with the neutral hollow mark.</summary>
    public static string StanceClass(string stance) => "choice choice--" + (stance is "for" or "against" ? stance : "remarks");

    /// <summary>
    /// The genitive after a number (Lithuanian): singular after a number ending in 1 but not 11 ("21 dalyvio"), plural
    /// otherwise ("1 000 dalyvių", "11 dalyvių"). The goal line uses it: "Siekiame {N} dalyvių".
    /// </summary>
    public static bool GenitiveSingular(int n) => NumberForms.Of(n) == NumberForm.One;

    public static string GoalKey(int goal) => GenitiveSingular(goal) ? "Org.Goal.One" : "Org.Goal.Many";

    /// <summary>The page of an organisation's profile.</summary>
    public static string ProfilePath(string slug) => "/o/" + Uri.EscapeDataString(slug);
}
