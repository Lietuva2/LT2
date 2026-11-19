using System.Globalization;
using System.Text;
using Lt2.Api.Contracts.Parliament;

namespace Lt2.Web.Client.Parliament;

/// <summary>One link of the row "by ballot": a choice (null = "Visi"), its word in the address and its count.</summary>
public sealed record BallotChoiceCount(string? Choice, string? Slug, int Count);

/// <summary>One option of the faction dropdown: its value in the address and its name (null name = MPs without a faction).</summary>
public sealed record BallotFaction(string Slug, string? Name);

/// <param name="Query">The search as used (trimmed), null when none.</param>
/// <param name="Faction">The faction chosen (its slug, or <see cref="BallotList.NoFaction"/>), null for all.</param>
/// <param name="Choice">The ballot chosen, an API code, null for all.</param>
/// <param name="Factions">The dropdown's factions: those with a ballot in the vote, by name; MPs without a faction last.</param>
/// <param name="Counts">"Visi" first, then each choice the vote has; counted within the search and the faction, so they add up.</param>
/// <param name="Shown">The rows to show, in the order of the MPs' last names.</param>
/// <param name="Matched">How many rows the search, the faction and the ballot find.</param>
/// <param name="More">The next value of <c>rodyti</c>, null when every row found is shown.</param>
public sealed record BallotListView(
    string? Query,
    string? Faction,
    string? Choice,
    IReadOnlyList<BallotFaction> Factions,
    IReadOnlyList<BallotChoiceCount> Counts,
    IReadOnlyList<BallotResponse> Shown,
    int Matched,
    int? More);

/// <summary>
/// Design 8.17: the list of the MPs' ballots on a vote's page, filtered on the server from the ballots the page already
/// has. The state is the page's address (<c>?q=</c>, <c>?frakcija=</c>, <c>?balsas=</c>, <c>?rodyti=</c>); a value the
/// page would not make falls back to the default, never to an error. The search finds by first or last name, ignoring
/// case and Lithuanian diacritics; every word of it must be found. The faction is chosen from a dropdown.
/// </summary>
public static class BallotList
{
    /// <summary>The rows shown first.</summary>
    public const int First = 20;

    /// <summary>The rows "Rodyti daugiau" adds (owner, 2026-11-19: a list of rows, 50 a press; it was 40).</summary>
    public const int Step = Lt2.Api.Contracts.ListPaging.RowStep;

    /// <summary>A longer search is not one the field makes; it is ignored.</summary>
    public const int MaxQueryLength = 100;

    /// <summary>The faction value of the MPs without a faction.</summary>
    public const string NoFaction = "be-frakcijos";

    private static readonly CultureInfo Lithuanian = CultureInfo.GetCultureInfo("lt-LT");
    private static readonly StringComparer ByLithuanian = StringComparer.Create(Lithuanian, ignoreCase: true);

    /// <summary>The ballot choices in the order of the links, with their words in the address.</summary>
    public static IReadOnlyList<(string Choice, string Slug)> Choices { get; } =
        [("for", "uz"), ("against", "pries"), ("abstain", "susilaike"), ("did-not-vote", "nebalsavo")];

    public static BallotListView Apply(
        IReadOnlyList<BallotResponse> ballots, IReadOnlyList<FactionSplitResponse> factions, string? query, string? faction, string? ballot, string? show)
    {
        ArgumentNullException.ThrowIfNull(ballots);
        ArgumentNullException.ThrowIfNull(factions);

        var bodies = factions.Where(f => f.Faction is not null).GroupBy(f => f.Label.Trim()).ToDictionary(g => g.Key, g => g.First().Faction!);
        var rows = ballots
            .Select(b => (Ballot: b, Faction: FactionOf(b, bodies)))
            .OrderBy(r => r.Ballot.Mp.LastName, ByLithuanian).ThenBy(r => r.Ballot.Mp.FirstName, ByLithuanian)
            .ToList();
        var options = rows.Select(r => r.Faction).Where(f => f.Name is not null).DistinctBy(f => f.Slug).OrderBy(f => f.Name, ByLithuanian).ToList();
        if (rows.Any(r => r.Faction.Name is null))
        {
            options.Add(new BallotFaction(NoFaction, null));
        }

        var factionSlug = options.Any(f => f.Slug == faction) ? faction : null;
        var q = string.IsNullOrWhiteSpace(query) || query.Trim().Length > MaxQueryLength ? null : query.Trim();
        var words = q is null ? [] : Fold(q).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var found = rows
            .Where(r => factionSlug is null || r.Faction.Slug == factionSlug)
            .Where(r => words.Length == 0 || Matches(Fold(r.Ballot.Mp.FirstName + " " + r.Ballot.Mp.LastName), words))
            .Select(r => r.Ballot)
            .ToList();

        var present = Choices.Where(c => ballots.Any(b => b.Choice == c.Choice)).ToList();
        var choice = present.Where(c => c.Slug == ballot).Select(c => c.Choice).FirstOrDefault();
        var counts = new List<BallotChoiceCount> { new(null, null, found.Count) };
        counts.AddRange(present.Select(c => new BallotChoiceCount(c.Choice, c.Slug, found.Count(b => b.Choice == c.Choice))));

        var matched = choice is null ? found : found.Where(b => b.Choice == choice).ToList();
        var shown = Shown(show, ballots.Count);
        int? more = shown < matched.Count ? Math.Min(shown + Step, matched.Count) : null;
        return new BallotListView(q, factionSlug, choice, options, counts, matched.Take(shown).ToList(), matched.Count, more);
    }

    /// <summary>
    /// The vote page's address with this state, landing on <paramref name="fragment"/>; defaults are left out, so the plain
    /// list is the plain address.
    /// </summary>
    public static string Address(long voteId, string? query, string? faction, string? choice, int? show, string fragment)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(query))
        {
            parts.Add("q=" + Uri.EscapeDataString(query.Trim()));
        }

        if (!string.IsNullOrEmpty(faction))
        {
            parts.Add("frakcija=" + Uri.EscapeDataString(faction));
        }

        if (SlugOf(choice) is { } slug)
        {
            parts.Add("balsas=" + slug);
        }

        if (show is { } n && n != First)
        {
            parts.Add("rodyti=" + n.ToString(CultureInfo.InvariantCulture));
        }

        var path = "/seimas/balsavimai/" + voteId.ToString(CultureInfo.InvariantCulture);
        return (parts.Count == 0 ? path : path + "?" + string.Join("&", parts)) + "#" + fragment;
    }

    /// <summary>The word of a choice in the address ("uz"), null for all.</summary>
    public static string? SlugOf(string? choice) => Choices.Where(c => c.Choice == choice).Select(c => c.Slug).FirstOrDefault();

    /// <summary>Lower case without diacritics: "Žolynaitė" is found by "zolynaite".</summary>
    public static string Fold(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var decomposed = text.Trim().ToLower(Lithuanian).Normalize(NormalizationForm.FormD);
        var folded = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                folded.Append(char.IsWhiteSpace(c) ? ' ' : c);
            }
        }

        return folded.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// A ballot's faction: the body the vote's faction positions name for its label (its slug and name); a label without
    /// a body is its own option (folded, "zyme-" before it, the label as the name); no label is <see cref="NoFaction"/>.
    /// </summary>
    private static BallotFaction FactionOf(BallotResponse ballot, Dictionary<string, BodyLink> bodies)
    {
        var label = ballot.FactionLabel?.Trim();
        if (string.IsNullOrEmpty(label))
        {
            return new BallotFaction(NoFaction, null);
        }

        return bodies.TryGetValue(label, out var body)
            ? new BallotFaction(body.Slug, body.Name)
            : new BallotFaction("zyme-" + Fold(label).Replace(' ', '-'), label);
    }

    /// <summary>The rows to show: <c>rodyti</c> when it is a whole number from <see cref="First"/> to the vote's ballots, else <see cref="First"/>.</summary>
    private static int Shown(string? show, int total) =>
        int.TryParse(show, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= First && n <= total ? n : First;

    private static bool Matches(string haystack, string[] words) => words.All(w => haystack.Contains(w, StringComparison.Ordinal));
}
