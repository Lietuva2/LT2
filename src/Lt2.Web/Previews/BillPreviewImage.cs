using SkiaSharp;

namespace Lt2.Web.Previews;

/// <summary>
/// Draws a bill's link-preview image (design 8.22.5) on the shared ground and mark (<see cref="PreviewDrawing"/>): the label
/// line, the title in at most four lines, the stage line and, while the record is open, the line inviting people to take part.
/// </summary>
internal static class BillPreviewImage
{
    /// <summary>The title's lines at most.</summary>
    public const int TitleLines = 4;

    /// <summary>The title's size.</summary>
    public const float TitleSize = 56;

    public static byte[] Draw(BillPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return PreviewDrawing.Draw((canvas, top, bottom) => DrawBody(canvas, preview, top, bottom));
    }

    /// <summary>The label, the title, the stage and the call, centred together between <paramref name="top"/> and <paramref name="bottom"/>.</summary>
    private static void DrawBody(SKCanvas canvas, BillPreview preview, float top, float bottom)
    {
        const float centre = PreviewDrawing.Centre;
        const float width = PreviewDrawing.ContentWidth;
        using var labelFont = new SKFont(PreviewDrawing.Text, 26);
        using var titleFont = new SKFont(PreviewDrawing.Display, TitleSize);
        using var callFont = new SKFont(PreviewDrawing.Bold, 26);
        var title = PreviewDrawing.Lines(preview.Title, titleFont, width, TitleLines);
        var call = preview.Call is null ? [] : PreviewDrawing.Lines(preview.Call, callFont, width, 2);
        const float labelLine = 32;
        const float titleLine = 60;
        const float smallLine = 32;
        var height = labelLine + 12 + title.Count * titleLine + (preview.Stage is null ? 0 : 14 + smallLine) + (call.Count == 0 ? 0 : 22 + call.Count * smallLine);
        var y = top + Math.Max(0, (bottom - top - height) / 2);

        using var ink2 = new SKPaint { IsAntialias = true, Color = PreviewDrawing.Ink2 };
        using var ink = new SKPaint { IsAntialias = true, Color = PreviewDrawing.Ink };
        using var brand = new SKPaint { IsAntialias = true, Color = PreviewDrawing.Brand };
        canvas.DrawText(PreviewDrawing.Fit(preview.Label, labelFont, width), centre, y + 26, SKTextAlign.Center, labelFont, ink2);
        y += labelLine + 12;
        foreach (var line in title)
        {
            canvas.DrawText(line, centre, y + 48, SKTextAlign.Center, titleFont, ink);
            y += titleLine;
        }

        if (preview.Stage is { } stage)
        {
            y += 14;
            canvas.DrawText(PreviewDrawing.Fit(stage, labelFont, width), centre, y + 26, SKTextAlign.Center, labelFont, ink2);
            y += smallLine;
        }

        if (call.Count > 0)
        {
            y += 22;
            foreach (var line in call)
            {
                canvas.DrawText(line, centre, y + 26, SKTextAlign.Center, callFont, brand);
                y += smallLine;
            }
        }
    }
}
