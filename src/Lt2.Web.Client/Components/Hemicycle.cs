using Lt2.Api.Contracts.Parliament;

namespace Lt2.Web.Client.Components;

/// <summary>
/// Seat positions of a hemicycle for the seat map (design 7.3: SVG, drawn by the server for a vote's page and by the browser on a
/// bill's page; a PNG for the vote's link preview, design 8.22.5). MPs sit where the Seimas's seating plan puts them
/// (<see cref="SeatingPlan"/>): the hall's four sectors with aisles between them, six rows each. A vote whose MPs the plan
/// does not seat (an earlier term) is drawn as a plain hemicycle with factions together (<see cref="Layout"/>).
/// Each seat is a small square turned to face the centre, as in the prototype (design/ui/index.html, drawHemi).
/// Coordinates are in a 200 × 104 view box with the centre at (100, 100).
/// </summary>
public static class Hemicycle
{
    public const double Width = 200;
    public const double Height = 104;
    private const double CentreX = 100;
    private const double CentreY = 100;
    private const double Outer = 96;
    private const double Inner = 36;

    /// <summary>The choices in the order the seats take them within a faction and the legend lists them.</summary>
    public static readonly IReadOnlyList<string> ChoiceOrder = [BallotChoices.For, BallotChoices.Against, BallotChoices.Abstain, BallotChoices.DidNotVote];

    public readonly record struct Seat(double X, double Y, double Angle, int Row);

    /// <summary>
    /// Each ballot on its seat. On the seating plan when it seats every MP of the vote but as many as it has free seats: those
    /// few (MPs who came after the plan) take its free seats, factions together as below. Otherwise a plain hemicycle where
    /// factions sit together, largest first, MPs without one last, and within a faction the choices in <see cref="ChoiceOrder"/>.
    /// </summary>
    public static (IReadOnlyList<(Seat Seat, BallotResponse Ballot)> Seats, double Side) Arrange(IReadOnlyList<BallotResponse> ballots)
    {
        ArgumentNullException.ThrowIfNull(ballots);
        var size = ballots.GroupBy(b => b.FactionLabel ?? "").ToDictionary(g => g.Key, g => g.Count());
        var ordered = ballots
            .OrderBy(b => string.IsNullOrEmpty(b.FactionLabel) ? 1 : 0)
            .ThenByDescending(b => size[b.FactionLabel ?? ""])
            .ThenBy(b => b.FactionLabel, StringComparer.Ordinal)
            .ThenBy(b => IndexOf(b.Choice))
            .ToList();
        var hall = Hall.Value;
        var unseated = ordered.Where(b => !hall.Seats.ContainsKey(b.Mp.Slug)).ToList();
        if (ordered.Count > 0 && unseated.Count <= hall.Free.Count && ordered.DistinctBy(b => b.Mp.Slug).Count() == ordered.Count)
        {
            var placed = ordered.Where(b => hall.Seats.ContainsKey(b.Mp.Slug)).Select(b => (hall.Seats[b.Mp.Slug], b))
                .Concat(hall.Free.Zip(unseated))
                .OrderByDescending(p => p.Item1.Angle).ThenBy(p => p.Item1.Row)
                .ToList();
            return (placed, hall.Side);
        }

        var (seats, side) = Layout(ordered.Count);
        return (seats.Zip(ordered).ToList(), side);
    }

    /// <summary>
    /// The hall as the plan draws it: the sectors' edges in degrees (0 is right, 180 left; the aisles are the gaps) and the rows'
    /// radii, the same in every sector. A row's seats share its sector's width evenly; the two middle sectors' front rows have two
    /// wider seats on the middle aisle's side.
    /// </summary>
    private static readonly (double From, double To)[] Sectors = [(163, 128), (124, 92), (88, 56), (52, 17)];

    private static readonly Lazy<(IReadOnlyDictionary<string, Seat> Seats, IReadOnlyList<Seat> Free, double Side)> Hall = new(BuildHall);

    private static (IReadOnlyDictionary<string, Seat> Seats, IReadOnlyList<Seat> Free, double Side) BuildHall()
    {
        var seats = new Dictionary<string, Seat>(StringComparer.Ordinal);
        var free = new List<Seat>();
        foreach (var (sector, row, names) in SeatingPlan.Rows)
        {
            var (from, to) = Sectors[sector - 1];
            var radius = RowRadius(row);
            // The middle sectors' two front seats stand in the two of three places nearest the middle aisle.
            var places = row == 1 && names.Count == 2 && sector is 2 or 3 ? 3 : names.Count;
            var first = sector == 2 && places > names.Count ? places - names.Count : 0;
            for (var j = 0; j < names.Count; j++)
            {
                var angle = (from + (to - from) * (first + j + 0.5) / places) * Math.PI / 180;
                var seat = new Seat(CentreX + radius * Math.Cos(angle), CentreY - radius * Math.Sin(angle), angle, row - 1);
                if (names[j].Length == 0)
                {
                    free.Add(seat);
                }
                else
                {
                    seats.Add(names[j], seat);
                }
            }
        }

        return (seats, free, HallSide);
    }

    private const int HallRows = 6;
    private const double HallInner = 40;

    /// <summary>The side of a seat's square in the hall: a little under the closest seats' spacing (the side sectors' front rows).</summary>
    private const double HallSide = 5;

    private static double RowRadius(int row) => HallInner + (Outer - HallInner) * (row - 1) / (HallRows - 1);

    private static int IndexOf(string choice)
    {
        for (var i = 0; i < ChoiceOrder.Count; i++)
        {
            if (ChoiceOrder[i] == choice)
            {
                return i;
            }
        }

        return -1;
    }

    /// <returns>The seats, read left to right, and the side of each seat's square (as the prototype's 13 at a 22 row step).</returns>
    public static (IReadOnlyList<Seat> Seats, double Side) Layout(int count)
    {
        if (count <= 0)
        {
            return ([], 0);
        }

        var rows = Math.Clamp((int)Math.Ceiling(Math.Sqrt(count / 3.0)), 1, 8);
        var radii = Enumerable.Range(0, rows).Select(i => Inner + (Outer - Inner) * (i + 0.5) / rows).ToArray();
        var total = radii.Sum();
        var perRow = radii.Select(r => (int)Math.Floor(count * r / total)).ToArray();
        for (var i = rows - 1; perRow.Sum() < count; i = i == 0 ? rows - 1 : i - 1)
        {
            perRow[i]++;
        }

        var seats = new List<Seat>(count);
        for (var row = 0; row < rows; row++)
        {
            var n = perRow[row];
            for (var j = 0; j < n; j++)
            {
                var angle = Math.PI - Math.PI * (j + 0.5) / n;
                seats.Add(new Seat(CentreX + radii[row] * Math.Cos(angle), CentreY - radii[row] * Math.Sin(angle), angle, row));
            }
        }

        var rowGap = (Outer - Inner) / rows;
        var arcGap = perRow.Select((n, i) => n == 0 ? double.MaxValue : Math.PI * radii[i] / n).Min();
        var side = Math.Min(rowGap, arcGap) * 0.6;
        return (seats.OrderByDescending(s => s.Angle).ThenBy(s => s.Row).ToList(), side);
    }
}
