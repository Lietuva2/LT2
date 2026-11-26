using Lt2.Api.Contracts.Parliament;

namespace Lt2.Web.Client.Components;

/// <summary>
/// Seat positions of a hemicycle for the seat map (design 7.3: SVG, drawn by the server for a vote's page and by the browser on a
/// bill's page; a PNG for the vote's link preview, design 8.22.5). The open data has no seat
/// positions, so the map places seats itself: rows of arcs, more seats on the longer outer rows, read left to right.
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
    /// Each ballot on its seat: factions sit together, largest first, MPs without one last (the open data has no seat order),
    /// and within a faction the choices in <see cref="ChoiceOrder"/>.
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
        var (seats, side) = Layout(ordered.Count);
        return (seats.Zip(ordered).ToList(), side);
    }

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
