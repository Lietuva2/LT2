using System.Globalization;
using Lt2.Api.Contracts.Parliament;
using Lt2.Web.Client.Parliament;

namespace Lt2.UnitTests;

/// <summary>Design 8.17: the MPs' ballots on a vote's page, searched by name, chosen by faction and ballot, shown a part at a time.</summary>
public sealed class BallotListTests
{
    private const string Rpf = "reformu-partijos-frakcija";
    private const string Psf = "pilietines-sajungos-frakcija";

    private static readonly string[] Choices = ["for", "against", "abstain", "did-not-vote"];

    private static readonly FactionSplitResponse[] Factions =
    [
        new("RPF", new BodyLink(Rpf, "Reformų partijos frakcija", "faction"), "for", 0, 0, 0, 0),
        new("PSF", new BodyLink(Psf, "Pilietinės sąjungos frakcija", "faction"), "against", 0, 0, 0, 0),
    ];

    /// <summary>141 invented ballots: four named MPs (one without a faction) and 137 more, the choices in turn.</summary>
    private static List<BallotResponse> Vote()
    {
        var ballots = new List<BallotResponse>
        {
            Ballot("Jonas", "Petraitis", "RPF", "against"),
            Ballot("Ona", "Jonaitienė", "PSF", "for"),
            Ballot("Marius", "Ąžuolaitis", "RPF", "for"),
            Ballot("Gintarė", "Šermukšnytė", null, "abstain"),
        };
        for (var i = 0; i < 137; i++)
        {
            ballots.Add(Ballot("Vardas" + i, "Bandomasis" + i.ToString("000", CultureInfo.InvariantCulture), i % 2 == 0 ? "RPF" : "PSF", Choices[i % 4]));
        }

        return ballots;
    }

    private static BallotResponse Ballot(string first, string last, string? faction, string choice) =>
        new(new MpLink((first + "-" + last).ToLowerInvariant(), first, last), faction, choice);

    private static BallotListView Apply(IReadOnlyList<BallotResponse> ballots, string? query = null, string? faction = null, string? ballot = null, string? show = null) =>
        BallotList.Apply(ballots, Factions, query, faction, ballot, show);

    [Fact]
    public void The_first_20_are_shown_and_each_more_adds_50_up_to_all()
    {
        var ballots = Vote();

        var first = Apply(ballots);
        first.Shown.Count.ShouldBe(20);
        first.Matched.ShouldBe(141);
        first.More.ShouldBe(70);

        var steps = new List<int>();
        for (var view = first; view.More is { } more; view = Apply(ballots, show: more.ToString(CultureInfo.InvariantCulture)))
        {
            steps.Add(more);
        }

        steps.ShouldBe([70, 120, 141]);
        var all = Apply(ballots, show: "141");
        all.Shown.Count.ShouldBe(141);
        all.More.ShouldBeNull();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-40")]
    [InlineData("0")]
    [InlineData("19")]
    [InlineData("142")]
    [InlineData("999999999999")]
    [InlineData(" 60")]
    [InlineData("60.0")]
    public void A_show_value_the_page_would_not_make_falls_back_to_the_first_20(string show) =>
        Apply(Vote(), show: show).Shown.Count.ShouldBe(20);

    [Fact]
    public void The_rows_are_in_the_order_of_the_last_names_in_the_lithuanian_alphabet()
    {
        var ballots = new[]
        {
            Ballot("Gintarė", "Šermukšnytė", "RPF", "for"),
            Ballot("Jonas", "Petraitis", "RPF", "for"),
            Ballot("Marius", "Ąžuolaitis", "RPF", "for"),
            Ballot("Eglė", "Sakalauskė", "RPF", "for"),
            Ballot("Indrė", "Žolynaitė", "RPF", "for"),
            Ballot("Laima", "Zuikytė", "RPF", "for"),
        };

        Apply(ballots).Shown.Select(b => b.Mp.LastName).ShouldBe(["Ąžuolaitis", "Petraitis", "Sakalauskė", "Šermukšnytė", "Zuikytė", "Žolynaitė"]);
    }

    [Fact]
    public void Each_ballot_shows_exactly_the_mps_with_that_ballot_and_the_counts_add_up_to_the_list()
    {
        var ballots = Vote();
        var all = Apply(ballots);

        all.Counts[0].ShouldBe(new BallotChoiceCount(null, null, 141));
        all.Counts.Skip(1).Select(c => c.Choice).ShouldBe(Choices);
        all.Counts.Skip(1).Sum(c => c.Count).ShouldBe(141);
        foreach (var count in all.Counts.Skip(1))
        {
            var view = Apply(ballots, ballot: count.Slug, show: "141");
            view.Choice.ShouldBe(count.Choice);
            view.Matched.ShouldBe(count.Count);
            view.Shown.ShouldAllBe(b => b.Choice == count.Choice);
            view.Shown.Count.ShouldBe(ballots.Count(b => b.Choice == count.Choice));
        }
    }

    [Fact]
    public void Only_the_ballots_the_vote_has_get_a_link()
    {
        var ballots = new[] { Ballot("Jonas", "Petraitis", "RPF", "against"), Ballot("Ona", "Jonaitienė", "PSF", "for") };

        Apply(ballots).Counts.Select(c => c.Slug).ShouldBe([null, "uz", "pries"]);
        Apply(ballots, ballot: "susilaike").Choice.ShouldBeNull("a ballot the vote does not have falls back to all");
    }

    [Theory]
    [InlineData("nera")]
    [InlineData("for")]
    [InlineData("UZ")]
    [InlineData("")]
    public void An_unknown_ballot_falls_back_to_all(string ballot)
    {
        var view = Apply(Vote(), ballot: ballot);

        view.Choice.ShouldBeNull();
        view.Matched.ShouldBe(141);
    }

    [Theory]
    [InlineData("zolynaite", "Žolynaitė")]
    [InlineData("ŽOLYNAITĖ", "Žolynaitė")]
    [InlineData("azuol", "Ąžuolaitis")]
    [InlineData("sermuksnyte", "Šermukšnytė")]
    [InlineData("  gintare  ", "Šermukšnytė")]
    [InlineData("petraitis jonas", "Petraitis")]
    public void A_search_ignores_case_and_lithuanian_diacritics(string query, string lastName)
    {
        var ballots = Vote();
        ballots.Add(Ballot("Indrė", "Žolynaitė", "PSF", "for"));

        var view = Apply(ballots, query);

        view.Matched.ShouldBe(1, query);
        view.Shown.Single().Mp.LastName.ShouldBe(lastName);
        view.Query.ShouldBe(query.Trim());
    }

    [Theory]
    [InlineData("rpf")]
    [InlineData("reformu partijos")]
    [InlineData("pilietines")]
    public void A_search_finds_by_name_only_never_by_faction(string query) =>
        Apply(Vote(), query).Matched.ShouldBe(0);

    [Fact]
    public void A_search_for_a_factions_name_finds_an_mp_whose_name_matches()
    {
        var ballots = Vote();
        ballots.Add(Ballot("Pilietina", "Reformaitė", "RPF", "for"));

        Apply(ballots, "pilietin").Shown.Single().Mp.LastName.ShouldBe("Reformaitė");
    }

    [Fact]
    public void The_dropdown_has_the_factions_with_a_ballot_by_name_and_the_mps_without_a_faction_last()
    {
        var ballots = Vote();
        ballots.Add(Ballot("Rasa", "Ievaitė", "MSNG", "for"));

        Apply(ballots).Factions.ShouldBe(
        [
            new BallotFaction("zyme-msng", "MSNG"),
            new BallotFaction(Psf, "Pilietinės sąjungos frakcija"),
            new BallotFaction(Rpf, "Reformų partijos frakcija"),
            new BallotFaction(BallotList.NoFaction, null),
        ]);
    }

    [Fact]
    public void Without_mps_without_a_faction_there_is_no_such_option()
    {
        var ballots = new[] { Ballot("Jonas", "Petraitis", "RPF", "against") };

        Apply(ballots).Factions.ShouldBe([new BallotFaction(Rpf, "Reformų partijos frakcija")]);
        Apply(ballots, faction: BallotList.NoFaction).Faction.ShouldBeNull();
    }

    [Fact]
    public void A_faction_shows_exactly_its_mps_and_the_counts_are_within_it()
    {
        var ballots = Vote();
        var rpf = ballots.Where(b => b.FactionLabel == "RPF").ToList();

        var view = Apply(ballots, faction: Rpf, show: "141");

        view.Faction.ShouldBe(Rpf);
        view.Matched.ShouldBe(rpf.Count);
        view.Shown.ShouldAllBe(b => b.FactionLabel == "RPF");
        view.Counts[0].Count.ShouldBe(rpf.Count);
        foreach (var count in view.Counts.Skip(1))
        {
            count.Count.ShouldBe(rpf.Count(b => b.Choice == count.Choice), count.Choice);
        }
    }

    [Fact]
    public void The_no_faction_option_shows_the_mps_without_a_faction()
    {
        var view = Apply(Vote(), faction: BallotList.NoFaction);

        view.Faction.ShouldBe(BallotList.NoFaction);
        view.Shown.Single().Mp.LastName.ShouldBe("Šermukšnytė");
    }

    [Theory]
    [InlineData("nera-tokios")]
    [InlineData("RPF")]
    [InlineData("")]
    public void An_unknown_faction_means_all_factions(string faction)
    {
        var view = Apply(Vote(), faction: faction);

        view.Faction.ShouldBeNull();
        view.Matched.ShouldBe(141);
    }

    [Fact]
    public void Search_faction_and_ballot_combine_and_the_counts_are_within_the_search_and_the_faction()
    {
        var ballots = Vote();
        ballots.Add(Ballot("Jonas", "Jonaitis", "PSF", "against"));
        var jonasRpf = ballots.Where(b => b.Mp.FirstName == "Jonas" && b.FactionLabel == "RPF").ToList();

        var view = Apply(ballots, "jonas", Rpf, "pries");

        view.Shown.Single().Mp.LastName.ShouldBe("Petraitis");
        view.Matched.ShouldBe(1);
        view.Counts[0].Count.ShouldBe(jonasRpf.Count);
        view.Counts.Skip(1).Sum(c => c.Count).ShouldBe(jonasRpf.Count);
        view.Counts.Single(c => c.Choice == "against").Count.ShouldBe(1);
        Apply(ballots, "jonas", Psf, "uz").Matched.ShouldBe(0, "Jonaitis (PSF) voted against");
    }

    [Fact]
    public void A_search_that_finds_nothing_shows_no_rows_and_no_more()
    {
        var view = Apply(Vote(), "niekoniekas", ballot: "uz");

        view.Matched.ShouldBe(0);
        view.Shown.ShouldBeEmpty();
        view.More.ShouldBeNull();
        view.Counts.ShouldAllBe(c => c.Count == 0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void An_empty_search_is_no_search(string? query) =>
        Apply(Vote(), query).Query.ShouldBeNull();

    [Fact]
    public void An_oversized_search_is_ignored() =>
        Apply(Vote(), new string('a', BallotList.MaxQueryLength + 1)).Matched.ShouldBe(141);

    [Fact]
    public void The_address_carries_only_what_differs_from_the_defaults()
    {
        BallotList.Address(99100002, null, null, null, null, "balsai").ShouldBe("/seimas/balsavimai/99100002#balsai");
        BallotList.Address(99100002, " Žolynaitė ", Psf, "abstain", 20, "balsai")
            .ShouldBe("/seimas/balsavimai/99100002?q=%C5%BDolynait%C4%97&frakcija=pilietines-sajungos-frakcija&balsas=susilaike#balsai");
        BallotList.Address(99100002, null, null, "did-not-vote", 70, "balsas-21").ShouldBe("/seimas/balsavimai/99100002?balsas=nebalsavo&rodyti=70#balsas-21");
    }

    [Fact]
    public void No_ballots_make_an_empty_list() =>
        Apply([], "x", Rpf, "uz", "60").ShouldSatisfyAllConditions(
            v => v.Matched.ShouldBe(0),
            v => v.Factions.ShouldBeEmpty(),
            v => v.Counts.ShouldBe([new BallotChoiceCount(null, null, 0)]),
            v => v.More.ShouldBeNull());
}
