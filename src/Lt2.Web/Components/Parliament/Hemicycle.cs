namespace Lt2.Web.Components.Parliament;

/// <summary>
/// Seat positions of a hemicycle for the seat map (design 7.3: SVG rendered on the server). The open data has no seat
/// positions, so the map places seats itself: rows of arcs, more seats on the longer outer rows, read left to right.
/// Coordinates are in a 200 × 104 view box with the centre at (100, 100).
/// </summary>
internal static class Hemicycle
{
    public const double Width = 200;
    public const double Height = 104;
    private const double CentreX = 100;
    private const double CentreY = 100;
    private const double Outer = 96;
    private const double Inner = 36;

    public readonly record struct Seat(double X, double Y, double Angle, int Row);

    public static (IReadOnlyList<Seat> Seats, double Radius) Layout(int count)
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
        var radius = Math.Min(rowGap, arcGap) * 0.42;
        return (seats.OrderByDescending(s => s.Angle).ThenBy(s => s.Row).ToList(), radius);
    }
}
