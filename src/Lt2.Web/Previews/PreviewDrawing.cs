using SkiaSharp;

namespace Lt2.Web.Previews;

/// <summary>
/// What the drawn link-preview images share (design 8.22.5): the 1200×630 size, the brand's ground, colours and band, the
/// logo with "Lietuva 2.0" at the top, the site's own fonts (tools/make_preview_fonts.py, carried inside the assembly, so
/// nothing depends on the machine's fonts) and text cut to whole lines. Everything sits inside the centred 630 square that
/// phones crop to; only the band along the bottom reaches the edges, as in the site's static preview images.
/// </summary>
internal static class PreviewDrawing
{
    public const int Width = 1200;
    public const int Height = 630;
    public const float Centre = Width / 2f;
    public const float ContentWidth = 600;
    public const float BandHeight = 36;

    // The colours of lt2.css (--ground, --ink, --ink-2, --brand) and the logo's three.
    public static readonly SKColor Ground = SKColor.Parse("#e6ebe7");
    public static readonly SKColor Ink = SKColor.Parse("#14211b");
    public static readonly SKColor Ink2 = SKColor.Parse("#414f48");
    public static readonly SKColor Slate = SKColor.Parse("#a7b1ac");
    public static readonly SKColor Leaf = SKColor.Parse("#7cc242");
    public static readonly SKColor Brand = SKColor.Parse("#1f6b3a");

    private static readonly Lazy<SKTypeface> DisplayFace = new(() => Load("SofiaSansExtraCondensed-ExtraBold.ttf"));
    private static readonly Lazy<SKTypeface> TextFace = new(() => Load("SofiaSans-Medium.ttf"));
    private static readonly Lazy<SKTypeface> BoldFace = new(() => Load("SofiaSans-Bold.ttf"));

    // The logo's three shapes, as in wwwroot/favicon.svg (view box 28 44 604 474).
    private static readonly (string Path, SKColor Colour)[] Logo =
    [
        ("M70 78L345 106L352 58L396 108L292 112L293 240L168 243L165 316L40 298Z", Slate),
        ("M318 142L556 130L558 190L618 192L566 256L570 352L330 368Z", Leaf),
        ("M200 265L292 270L300 396L478 390L532 466L476 470L474 506L186 490Z", Brand),
    ];

    /// <summary>The titles' typeface.</summary>
    public static SKTypeface Display => DisplayFace.Value;

    /// <summary>The label lines' typeface.</summary>
    public static SKTypeface Text => TextFace.Value;

    public static SKTypeface Bold => BoldFace.Value;

    /// <summary>
    /// A PNG of the ground, the band and the mark, with <paramref name="body"/> drawing the rest between the y below the
    /// mark and the y above the band.
    /// </summary>
    public static byte[] Draw(Action<SKCanvas, float, float> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        using var bitmap = new SKBitmap(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(Ground);
            DrawBand(canvas);
            var top = DrawMark(canvas, 30);
            body(canvas, top + 16, Height - BandHeight - 18);
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
        const float logoHeight = 50;
        const float scale = logoHeight / 474f;
        const float logoWidth = 604 * scale;
        const float gap = 14;
        using var font = new SKFont(Display, 44);
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

    /// <summary>The text in at most <paramref name="max"/> lines of whole words, the last one cut with "…" when it does not fit.</summary>
    public static IReadOnlyList<string> Lines(string text, SKFont font, float width, int max)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);
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

    /// <summary>
    /// The text in as many lines as <see cref="Lines"/> takes, but balanced: the narrowest width that still takes no more lines
    /// and cuts nothing, so a title does not leave one word alone on a line.
    /// </summary>
    public static IReadOnlyList<string> Balanced(string text, SKFont font, float width, int max)
    {
        var lines = Lines(text, font, width, max);
        if (lines.Count < 2 || lines[^1].EndsWith('…'))
        {
            return lines;
        }

        float low = width / 2, high = width;
        var best = lines;
        while (high - low > 4)
        {
            var mid = (low + high) / 2;
            var tried = Lines(text, font, mid, max);
            if (tried.Count == lines.Count && !tried.Any(l => l.EndsWith('…')) && tried.All(l => font.MeasureText(l) <= mid))
            {
                best = tried;
                high = mid;
            }
            else
            {
                low = mid;
            }
        }

        return best;
    }

    /// <summary>The font size at most <paramref name="size"/> and at least <paramref name="least"/> at which the text fits the width.</summary>
    public static float SizeToFit(string text, SKTypeface face, float width, float size, float least)
    {
        using var font = new SKFont(face, size);
        while (font.Size > least && font.MeasureText(text) > width)
        {
            font.Size -= 1;
        }

        return font.Size;
    }

    /// <summary>The text as it is when it fits, otherwise cut at a word with "…".</summary>
    public static string Fit(string text, SKFont font, float width)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);
        if (font.MeasureText(text) <= width)
        {
            return text;
        }

        var cut = text;
        while (cut.Length > 1 && font.MeasureText(cut.TrimEnd(',', ';', ':', '.', ' ', '–', '-', '·') + "…") > width)
        {
            var space = cut.LastIndexOf(' ');
            cut = space > 0 ? cut[..space] : cut[..^1];
        }

        return cut.TrimEnd(',', ';', ':', '.', ' ', '–', '-', '·') + "…";
    }

    private static SKTypeface Load(string file)
    {
        using var stream = typeof(PreviewDrawing).Assembly.GetManifestResourceStream("Lt2.Web.Previews.Fonts." + file)
            ?? throw new InvalidOperationException($"The font {file} is not in the assembly.");
        return SKTypeface.FromStream(stream) ?? throw new InvalidOperationException($"The font {file} did not load.");
    }
}
