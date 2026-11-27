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
    private const float MapWidth = 440;

    /// <summary>The title's size.</summary>
    public const float TitleSize = 56;

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

    /// <summary>
    /// The label, the title, the seat map and the legend, centred together between <paramref name="top"/> and
    /// <paramref name="bottom"/>. Sized for a phone, where the image shows at about a third: no text under 28 px.
    /// </summary>
    private static void DrawBody(SKCanvas canvas, VotePreview preview, float top, float bottom)
    {
        // The label keeps the stage and the day: smaller, down to 28 px, and without "Seimo balsavimas" (the seat map says so)
        // when it still does not fit.
        var label = preview.Label;
        using (var least = new SKFont(PreviewDrawing.Text, 28))
        {
            if (least.MeasureText(label) > ContentWidth && label.IndexOf(" · ", StringComparison.Ordinal) is var cut and > 0)
            {
                label = label[(cut + 3)..];
            }
        }

        using var labelFont = new SKFont(PreviewDrawing.Text, PreviewDrawing.SizeToFit(label, PreviewDrawing.Text, ContentWidth, 32, 28));
        using var titleFont = new SKFont(PreviewDrawing.Display, TitleSize);
        using var legendFont = new SKFont(PreviewDrawing.Bold, 32);
        var title = PreviewDrawing.Balanced(preview.Title, titleFont, ContentWidth, 2);
        var legend = LegendRows(preview.Legend, legendFont);
        const float labelLine = 38;
        const float titleLine = 60;
        const float legendLine = 42;
        var mapWidth = legend.Count > 1 ? MapWidth - 40 : MapWidth;
        var mapHeight = mapWidth * (float)(Hemicycle.Height / Hemicycle.Width);
        var height = labelLine + 8 + title.Count * titleLine + 12 + mapHeight + 14 + legend.Count * legendLine;
        var y = top + Math.Max(0, (bottom - top - height) / 2);

        using var ink2 = new SKPaint { IsAntialias = true, Color = Ink2 };
        using var ink = new SKPaint { IsAntialias = true, Color = Ink };
        canvas.DrawText(PreviewDrawing.Fit(label, labelFont, ContentWidth), Centre, y + 31, SKTextAlign.Center, labelFont, ink2);
        y += labelLine + 8;
        foreach (var line in title)
        {
            canvas.DrawText(line, Centre, y + 48, SKTextAlign.Center, titleFont, ink);
            y += titleLine;
        }

        y += 12;
        DrawSeats(canvas, preview.Seats, preview.Side, Centre - mapWidth / 2, y, mapWidth / (float)Hemicycle.Width);
        y += mapHeight + 14;
        foreach (var row in legend)
        {
            DrawLegend(canvas, row, legendFont, y + 32);
            y += legendLine;
        }
    }

    private static void DrawSeats(SKCanvas canvas, IReadOnlyList<(Hemicycle.Seat Seat, string Choice)> seats, double side, float left, float top, float scale)
    {
        var half = (float)side / 2;
        using var paint = new SKPaint { IsAntialias = true };
        foreach (var (seat, choice) in seats)
        {
            paint.Color = ColourOf(choice);
            canvas.Save();
            canvas.Translate(left + (float)seat.X * scale, top + (float)seat.Y * scale);
            canvas.Scale(scale);
            canvas.RotateDegrees((float)(90 - seat.Angle * 180 / Math.PI));
            canvas.DrawRect(-half, -half, (float)side, (float)side, paint);
            canvas.Restore();
        }
    }

    private const float Square = 24;
    private const float AfterSquare = 10;
    private const float Between = 28;

    private static string LegendText(VotePreviewChoice c) => $"{c.Label} {Client.Display.Number(c.Count)}";

    private static float LegendWidth(VotePreviewChoice c, SKFont font) => Square + AfterSquare + font.MeasureText(LegendText(c));

    /// <summary>The legend's items in rows that fit the content's width, as few rows as that takes.</summary>
    internal static IReadOnlyList<IReadOnlyList<VotePreviewChoice>> LegendRows(IReadOnlyList<VotePreviewChoice> legend, SKFont font)
    {
        var rows = new List<List<VotePreviewChoice>>();
        var width = 0f;
        foreach (var item in legend)
        {
            var w = LegendWidth(item, font);
            if (rows.Count == 0 || width + Between + w > ContentWidth)
            {
                rows.Add([item]);
                width = w;
            }
            else
            {
                rows[^1].Add(item);
                width += Between + w;
            }
        }

        return rows;
    }

    private static void DrawLegend(SKCanvas canvas, IReadOnlyList<VotePreviewChoice> row, SKFont font, float baseline)
    {
        var widths = row.Select(c => LegendWidth(c, font)).ToList();
        var x = Centre - (widths.Sum() + Between * Math.Max(0, row.Count - 1)) / 2;
        using var ink = new SKPaint { IsAntialias = true, Color = Ink };
        using var fill = new SKPaint { IsAntialias = true };
        for (var i = 0; i < row.Count; i++)
        {
            fill.Color = ColourOf(row[i].Code);
            canvas.DrawRect(x, baseline - Square + 2, Square, Square, fill);
            canvas.DrawText(LegendText(row[i]), x + Square + AfterSquare, baseline, SKTextAlign.Left, font, ink);
            x += widths[i] + Between;
        }
    }
}
