using Lt2.Api.Contracts.Parliament;
using Lt2.Web.Client.Components;
using SkiaSharp;

namespace Lt2.Web.Previews;

/// <summary>
/// Draws a vote's link-preview image (design 8.22.5) on the shared ground and mark (<see cref="PreviewDrawing"/>): the label
/// line, the title (at most two lines), the seat map and its legend.
/// </summary>
internal static class VotePreviewImage
{
    private const float Centre = PreviewDrawing.Centre;
    private const float ContentWidth = PreviewDrawing.ContentWidth;
    private const float MapWidth = 500;

    private static SKColor Ink => PreviewDrawing.Ink;

    private static SKColor Ink2 => PreviewDrawing.Ink2;

    public static SKColor ColourOf(string choice) => choice switch
    {
        BallotChoices.For => SKColor.Parse("#1b6034"),
        BallotChoices.Against => SKColor.Parse("#85322f"),
        BallotChoices.Abstain => SKColor.Parse("#8a948e"),
        _ => SKColor.Parse("#cbd1cd"),
    };

    public static byte[] Draw(VotePreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return PreviewDrawing.Draw((canvas, top, bottom) => DrawBody(canvas, preview, top, bottom));
    }

    /// <summary>The label, the title, the seat map and the legend, centred together between <paramref name="top"/> and <paramref name="bottom"/>.</summary>
    private static void DrawBody(SKCanvas canvas, VotePreview preview, float top, float bottom)
    {
        using var labelFont = new SKFont(PreviewDrawing.Text, 24);
        using var titleFont = new SKFont(PreviewDrawing.Display, 48);
        using var legendFont = new SKFont(PreviewDrawing.Bold, 24);
        var title = PreviewDrawing.Lines(preview.Title, titleFont, ContentWidth, 2);
        const float labelLine = 30;
        const float titleLine = 52;
        const float mapHeight = MapWidth * (float)(Hemicycle.Height / Hemicycle.Width);
        const float legendLine = 30;
        var height = labelLine + 10 + title.Count * titleLine + 16 + mapHeight + 18 + legendLine;
        var y = top + Math.Max(0, (bottom - top - height) / 2);

        using var ink2 = new SKPaint { IsAntialias = true, Color = Ink2 };
        using var ink = new SKPaint { IsAntialias = true, Color = Ink };
        canvas.DrawText(PreviewDrawing.Fit(preview.Label, labelFont, ContentWidth), Centre, y + 24, SKTextAlign.Center, labelFont, ink2);
        y += labelLine + 10;
        foreach (var line in title)
        {
            canvas.DrawText(line, Centre, y + 42, SKTextAlign.Center, titleFont, ink);
            y += titleLine;
        }

        y += 16;
        DrawSeats(canvas, preview.Choices, Centre - MapWidth / 2, y, MapWidth / (float)Hemicycle.Width);
        y += mapHeight + 18;
        DrawLegend(canvas, preview.Legend, legendFont, y + 24);
    }

    private static void DrawSeats(SKCanvas canvas, IReadOnlyList<string> choices, float left, float top, float scale)
    {
        var (seats, side) = Hemicycle.Layout(choices.Count);
        var half = (float)side / 2;
        using var paint = new SKPaint { IsAntialias = true };
        for (var i = 0; i < seats.Count; i++)
        {
            var seat = seats[i];
            paint.Color = ColourOf(choices[i]);
            canvas.Save();
            canvas.Translate(left + (float)seat.X * scale, top + (float)seat.Y * scale);
            canvas.Scale(scale);
            canvas.RotateDegrees((float)(90 - seat.Angle * 180 / Math.PI));
            canvas.DrawRect(-half, -half, (float)side, (float)side, paint);
            canvas.Restore();
        }
    }

    private static void DrawLegend(SKCanvas canvas, IReadOnlyList<VotePreviewChoice> legend, SKFont font, float baseline)
    {
        const float square = 18;
        const float afterSquare = 8;
        const float between = 24;
        var items = legend.Select(c => (c.Code, Text: $"{c.Label} {Client.Display.Number(c.Count)}")).ToList();
        var widths = items.Select(i => square + afterSquare + font.MeasureText(i.Text)).ToList();
        var x = Centre - (widths.Sum() + between * Math.Max(0, items.Count - 1)) / 2;
        using var ink = new SKPaint { IsAntialias = true, Color = Ink };
        using var fill = new SKPaint { IsAntialias = true };
        for (var i = 0; i < items.Count; i++)
        {
            fill.Color = ColourOf(items[i].Code);
            canvas.DrawRect(x, baseline - square, square, square, fill);
            canvas.DrawText(items[i].Text, x + square + afterSquare, baseline, SKTextAlign.Left, font, ink);
            x += widths[i] + between;
        }
    }
}
