using Lt2.Api.Contracts.Parliament;
using Lt2.Web.Client.Components;

namespace Lt2.UnitTests;

/// <summary>Design 7.3: the seat map seats MPs where the Seimas's seating plan puts them, or factions together without it.</summary>
public sealed class HemicycleTests
{
    private static readonly string[] Choices = ["for", "against", "abstain", "did-not-vote"];

    private static IEnumerable<string> PlanSlugs => SeatingPlan.Rows.SelectMany(r => r.Seats).Where(s => s.Length > 0);

    private static BallotResponse Ballot(string slug, int i, string? faction = "RPF") =>
        new(new MpLink(slug, "Vardas", "Pavardė"), faction, Choices[i % Choices.Length]);

    private static List<BallotResponse> PlanVote() => PlanSlugs.Select((s, i) => Ballot(s, i)).ToList();

    [Fact]
    public void The_plan_seats_each_MP_once_in_four_sectors_of_six_rows()
    {
        PlanSlugs.Count().ShouldBe(140);
        PlanSlugs.Distinct().Count().ShouldBe(140);
        SeatingPlan.Rows.SelectMany(r => r.Seats).Count(s => s.Length == 0).ShouldBe(2);
        SeatingPlan.Rows.Select(r => (r.Sector, r.Row)).Distinct().Count().ShouldBe(SeatingPlan.Rows.Count);
        SeatingPlan.Rows.Select(r => r.Sector).Distinct().Order().ShouldBe([1, 2, 3, 4]);
        SeatingPlan.Rows.Select(r => r.Row).Distinct().Order().ShouldBe([1, 2, 3, 4, 5, 6]);
        PlanSlugs.ShouldAllBe(s => s == Lt2.SharedKernel.AddressSlugs.Of(s));
    }

    [Fact]
    public void MPs_on_the_plan_sit_in_their_row_and_sector()
    {
        var (seats, side) = Hemicycle.Arrange(PlanVote());

        seats.Count.ShouldBe(140);
        var rowOf = SeatingPlan.Rows.SelectMany(r => r.Seats.Where(s => s.Length > 0).Select(s => (s, r.Row, r.Sector))).ToDictionary(x => x.s);
        foreach (var (seat, ballot) in seats)
        {
            seat.Row.ShouldBe(rowOf[ballot.Mp.Slug].Row - 1);
        }

        // Left to right: sector 1's MPs before sector 2's, and so on.
        seats.Select(s => rowOf[s.Ballot.Mp.Slug].Sector).ShouldBeInOrder();
        side.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Seats_in_the_hall_do_not_overlap()
    {
        var (seats, side) = Hemicycle.Arrange(PlanVote().Append(Ballot("vardas-pavarde", 0)).Append(Ballot("kitas-vardas", 1)).ToList());

        seats.Count.ShouldBe(142);
        var closest = seats.SelectMany((a, i) => seats.Skip(i + 1).Select(b => Math.Sqrt(Math.Pow(a.Seat.X - b.Seat.X, 2) + Math.Pow(a.Seat.Y - b.Seat.Y, 2)))).Min();
        closest.ShouldBeGreaterThan(side * 1.2);
    }

    [Fact]
    public void An_MP_the_plan_does_not_seat_takes_one_of_its_free_seats()
    {
        var (seats, _) = Hemicycle.Arrange(PlanVote().Append(Ballot("vardas-pavarde", 0)).ToList());

        seats.Count.ShouldBe(141);
        var newcomer = seats.Single(s => s.Ballot.Mp.Slug == "vardas-pavarde").Seat;
        seats.Count(s => s.Seat == newcomer).ShouldBe(1);
    }

    [Fact]
    public void A_vote_with_more_MPs_off_the_plan_than_free_seats_seats_factions_together()
    {
        var ballots = PlanVote().Take(120)
            .Concat(Enumerable.Range(0, 21).Select(i => Ballot($"vardas-{i}", i, i % 2 == 0 ? "PSF" : null)))
            .ToList();

        var (seats, side) = Hemicycle.Arrange(ballots);

        var (plain, plainSide) = Hemicycle.Layout(141);
        seats.Select(s => s.Seat).ShouldBe(plain);
        side.ShouldBe(plainSide);
        // Factions together, largest first, MPs without one last.
        seats.Select(s => s.Ballot.FactionLabel ?? "").Distinct().ShouldBe(["RPF", "PSF", ""]);
    }
}
