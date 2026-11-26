using Lt2.Api.Contracts.Parliament;
using Lt2.Web.Client.Components;
using SkiaSharp;

namespace Lt2.Web.Previews;

/// <summary>
/// Draws a vote's link-preview image (design 8.22.5): 1200×630 PNG on the brand's ground, the logo and the name at the top,
/// the label line, the title (at most two lines), the seat map and its legend, all inside the centred 630 square that phones
/// crop to; only the brand's band along the bottom reaches the edges, as in the site's other preview images. The fonts are
/// the site's own (tools/make_preview_fonts.py), carried inside the assembly, so nothing depends on the machine's fonts.
/// </summary>
internal static class VotePreviewImage
{
    public const int Width = 1200;
    public const int Height = 630;

    private const float Centre = Width / 2f;
    private const float ContentWidth = 600;
    private const float BandHeight = 36;
    private const float MapWidth = 500;

    // The colours of lt2.css (--ground, --ink, --ink-2, the vote tokens) and the band of the other preview images.
    private static readonly SKColor Ground = SKColor.Parse("#e6ebe7");
    private static readonly SKColor Ink = SKColor.Parse("#14211b");
    private static readonly SKColor Ink2 = SKColor.Parse("#414f48");
    private static readonly SKColor Slate = SKColor.Parse("#a7b1ac");
    private static readonly SKColor Leaf = SKColor.Parse("#7cc242");
    private static readonly SKColor Brand = SKColor.Parse("#1f6b3a");

    private static readonly Lazy<SKTypeface> Display = new(() => Load("SofiaSansExtraCondensed-ExtraBold.ttf"));
    private static readonly Lazy<SKTypeface> Text = new(() => Load("SofiaSans-Medium.ttf"));
    private static readonly Lazy<SKTypeface> Bold = new(() => Load("SofiaSans-Bold.ttf"));

    // The logo's three shapes, as in wwwroot/favicon.svg (view box 28 44 604 474).
    private static readonly (string Path, SKColor Colour)[] Logo =
    [
        ("M70 78L345 106L352 58L396 108L292 112L293 240L168 243L165 316L40 298Z", Slate),
        ("M318 142L556 130L558 190L618 192L566 256L570 352L330 368Z", Leaf),
        ("M200 265L292 270L300 396L478 390L532 466L476 470L474 506L186 490Z", Brand),
    ];

    /// <summary>The title's typeface.</summary>
    internal static SKTypeface TitleFace => Display.Value;

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
        using var bitmap = new SKBitmap(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(Ground);
            DrawBand(canvas);
            var top = DrawMark(canvas, 30);
            DrawBody(canvas, preview, top + 16, Height - BandHeight - 18);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    private static void DrawBand(SKCanvas canvas)
    {
        using var paint = new SKPaint { IsAntialias = false };
        var y = Height - BandHeight;
        foreach (var (colour, i) in new[] { Slate, Leaf, Brand }.Select((c, i) => (c, i)))
        {
            paint.Color = colour;
            canvas.DrawRect(Width / 3f * i, y, Width / 3f, BandHeight, paint);
        }
    }

    /// <summary>The logo and "Lietuva 2.0", centred; returns the y below them.</summary>
    private static float DrawMark(SKCanvas canvas, float top)
    {
        const float logoHeight = 44;
        const float scale = logoHeight / 474f;
        const float logoWidth = 604 * scale;
        const float gap = 14;
        using var font = new SKFont(Display.Value, 38);
        using var ink = new SKPaint { IsAntialias = true, Color = Ink };
        const string name = "Lietuva 2.0";
        var nameWidth = font.MeasureText(name);
        var left = Centre - (logoWidth + gap + nameWidth) / 2;

        canvas.Save();
        canvas.Translate(left, top);
        canvas.Scale(scale);
        canvas.Translate(-28, -44);
        foreach (var (data, colour) in Logo)
        {
            using var path = SKPath.ParseSvgPathData(data);
            using var fill = new SKPaint { IsAntialias = true, Color = colour, Style = SKPaintStyle.StrokeAndFill, StrokeWidth = 10, StrokeJoin = SKStrokeJoin.Round };
            canvas.DrawPath(path, fill);
        }

        canvas.Restore();
        font.GetFontMetrics(out var metrics);
        var baseline = top + logoHeight / 2 - (metrics.Ascent + metrics.Descent) / 2;
        canvas.DrawText(name, left + logoWidth + gap, baseline, SKTextAlign.Left, font, ink);
        return top + logoHeight;
    }

    /// <summary>The label, the title, the seat map and the legend, centred together between <paramref name="top"/> and <paramref name="bottom"/>.</summary>
    private static void DrawBody(SKCanvas canvas, VotePreview preview, float top, float bottom)
    {
        using var labelFont = new SKFont(Text.Value, 24);
        using var titleFont = new SKFont(Display.Value, 48);
        using var legendFont = new SKFont(Bold.Value, 24);
        var title = Lines(preview.Title, titleFont, ContentWidth, 2);
        const float labelLine = 30;
        const float titleLine = 52;
        const float mapHeight = MapWidth * (float)(Hemicycle.Height / Hemicycle.Width);
        const float legendLine = 30;
        var height = labelLine + 10 + title.Count * titleLine + 16 + mapHeight + 18 + legendLine;
        var y = top + Math.Max(0, (bottom - top - height) / 2);

        using var ink2 = new SKPaint { IsAntialias = true, Color = Ink2 };
        using var ink = new SKPaint { IsAntialias = true, Color = Ink };
        canvas.DrawText(Fit(preview.Label, labelFont, ContentWidth), Centre, y + 24, SKTextAlign.Center, labelFont, ink2);
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

    /// <summary>The text in at most <paramref name="max"/> lines of whole words, the last one cut with "…" when it does not fit.</summary>
    internal static IReadOnlyList<string> Lines(string text, SKFont font, float width, int max)
    {
        var lines = new List<string>();
        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (lines.Count > 0 && font.MeasureText(lines[^1] + " " + word) <= width)
            {
                lines[^1] += " " + word;
            }
            else
            {
                lines.Add(word);
            }
        }

        if (lines.Count > max)
        {
            lines = [.. lines.Take(max - 1), string.Join(' ', lines.Skip(max - 1))];
        }

        return [.. lines.Select(l => Fit(l, font, width))];
    }

    /// <summary>The text as it is when it fits, otherwise cut at a word with "…".</summary>
    private static string Fit(string text, SKFont font, float width)
    {
        if (font.MeasureText(text) <= width)
        {
            return text;
        }

        var cut = text;
        while (cut.Length > 1 && font.MeasureText(cut.TrimEnd(',', ';', ':', ' ', '–', '-', '·') + "…") > width)
        {
            var space = cut.LastIndexOf(' ');
            cut = space > 0 ? cut[..space] : cut[..^1];
        }

        return cut.TrimEnd(',', ';', ':', ' ', '–', '-', '·') + "…";
    }

    private static SKTypeface Load(string file)
    {
        using var stream = typeof(VotePreviewImage).Assembly.GetManifestResourceStream("Lt2.Web.Previews.Fonts." + file)
            ?? throw new InvalidOperationException($"The font {file} is not in the assembly.");
        return SKTypeface.FromStream(stream) ?? throw new InvalidOperationException($"The font {file} did not load.");
    }
}
