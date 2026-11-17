// One source file, compiled twice, as TextExcerpt.cs: into Lt2.SharedKernel for the modules (the introduction letter's email)
// and, linked by Lt2.Web.Client.csproj with LT2_CLIENT defined, into the browser's assembly for the pages.
#if LT2_CLIENT
namespace Lt2.Web.Client;
#else
namespace Lt2.SharedKernel;
#endif

/// <summary>What a block of a typed text is: a paragraph, a bulleted list or a numbered list.</summary>
public enum TextBlockKind
{
    Paragraph,
    Bullets,
    Numbered,
}

/// <summary>A paragraph's text, or one item of a list without its marker.</summary>
/// <param name="Text">The line as typed, without the list's marker.</param>
/// <param name="Number">For a numbered item, the number as typed; otherwise null.</param>
/// <param name="Start">Where <paramref name="Text"/> starts in the text (line endings unified to <c>\n</c>).</param>
public sealed record TextItem(string Text, int? Number, int Start)
{
    /// <summary>The end of an item cut by "Rodyti daugiau": it continues the item before the cut, so it has no marker of its own.</summary>
    public bool Continues { get; init; }

    /// <summary>Where <see cref="Text"/> ends in the text.</summary>
    public int End => Start + Text.Length;
}

/// <summary>A paragraph (one item) or a list (its items, in order).</summary>
public sealed record TextBlock(TextBlockKind Kind, IReadOnlyList<TextItem> Items);

/// <summary>A text laid out: the blocks shown at first and, when the text is long, the blocks behind "Rodyti daugiau".</summary>
public sealed record TextLayoutParts(IReadOnlyList<TextBlock> Shown, IReadOnlyList<TextBlock>? Rest);

/// <summary>
/// A typed text's layout (design 8.2, "A text's layout", owner 2026-11-17): people type plain text, and paragraphs and lists are
/// read from it by fixed rules, never parsed as markup. Each line is a paragraph (empty lines add nothing). Two or more lines in
/// a row beginning "- ", "– ", "• " or "* " are a bulleted list; two or more beginning with a number (one to three digits) and
/// ". " or ") " are a numbered list, the numbers as typed. "-", "–", "•" and the number may also stand right before a word
/// ("-pirma", "1.pirma"; owner, 2026-11-17). One such line alone stays a paragraph, marker and all. One level
/// only. The text itself is never changed: what is stored, counted and screened is what was typed. A pure function, the same on
/// the server, in the browser and in the email.
/// </summary>
public static class TextLayout
{
    /// <summary>The blocks of <paramref name="text"/>, in order.</summary>
    public static IReadOnlyList<TextBlock> Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = Lines(Unified(text));
        var blocks = new List<TextBlock>();
        for (var i = 0; i < lines.Count;)
        {
            var kind = lines[i].Kind;
            var run = 1;
            while (kind != TextBlockKind.Paragraph && i + run < lines.Count && lines[i + run].Kind == kind)
            {
                run++;
            }

            if (run >= 2)
            {
                blocks.Add(new TextBlock(kind, lines.Skip(i).Take(run).Select(l => l.Item).ToList()));
            }
            else
            {
                // A paragraph, or one marked line alone: it stays as typed, marker included.
                blocks.Add(new TextBlock(TextBlockKind.Paragraph, [lines[i].Whole]));
            }

            i += run;
        }

        return blocks;
    }

    /// <summary>
    /// The text laid out and cut as <see cref="TextExcerpt.Of"/> cuts it: the whole text is laid out first, so a list cut by
    /// "Rodyti daugiau" stays a list on both sides and a numbered one keeps its numbers.
    /// </summary>
    public static TextLayoutParts Of(string text, int shown, int margin)
    {
        ArgumentNullException.ThrowIfNull(text);
        var unified = Unified(text);
        var blocks = Of(unified);
        var excerpt = TextExcerpt.Of(unified, shown, margin);
        if (excerpt.Rest is null)
        {
            return new TextLayoutParts(blocks, null);
        }

        var shownEnd = excerpt.Shown.Length;
        var restStart = unified.Length - excerpt.Rest.Length;
        var first = new List<TextBlock>();
        var rest = new List<TextBlock>();
        foreach (var block in blocks)
        {
            var before = new List<TextItem>();
            var after = new List<TextItem>();
            foreach (var item in block.Items)
            {
                if (item.End <= shownEnd)
                {
                    before.Add(item);
                }
                else if (item.Start >= restStart)
                {
                    after.Add(item);
                }
                else
                {
                    // The cut falls inside this item: its beginning is shown, its end follows behind "Rodyti daugiau".
                    var head = unified[item.Start..Math.Max(item.Start, shownEnd)].TrimEnd();
                    var tailFrom = Math.Min(Math.Max(restStart, item.Start), item.End);
                    var tail = unified[tailFrom..item.End];
                    if (head.Length > 0)
                    {
                        before.Add(item with { Text = head });
                    }

                    if (tail.Length > 0)
                    {
                        after.Add(new TextItem(tail, head.Length > 0 ? null : item.Number, tailFrom) { Continues = head.Length > 0 });
                    }
                }
            }

            if (before.Count > 0)
            {
                first.Add(block with { Items = before });
            }

            if (after.Count > 0)
            {
                rest.Add(block with { Items = after });
            }
        }

        return rest.Count == 0 ? new TextLayoutParts(blocks, null) : new TextLayoutParts(first, rest);
    }

    private static string Unified(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private sealed record Line(TextBlockKind Kind, TextItem Item, TextItem Whole);

    private static List<Line> Lines(string text)
    {
        var lines = new List<Line>();
        var at = 0;
        while (at <= text.Length)
        {
            var end = text.IndexOf('\n', at);
            if (end < 0)
            {
                end = text.Length;
            }

            var start = at;
            var stop = end;
            while (start < stop && char.IsWhiteSpace(text[start]))
            {
                start++;
            }

            while (stop > start && char.IsWhiteSpace(text[stop - 1]))
            {
                stop--;
            }

            if (stop > start)
            {
                var whole = new TextItem(text[start..stop], null, start);
                lines.Add(Classify(text, start, stop, whole));
            }

            at = end + 1;
        }

        return lines;
    }

    private static Line Classify(string text, int start, int stop, TextItem whole)
    {
        var c = text[start];
        if (c is '-' or '–' or '•' or '*' && start + 1 < stop
            && (char.IsWhiteSpace(text[start + 1]) || (c != '*' && StartsWords(text[start + 1]))))
        {
            var from = Skip(text, start + 1, stop);
            if (from < stop)
            {
                return new Line(TextBlockKind.Bullets, new TextItem(text[from..stop], null, from), whole);
            }
        }

        var digits = 0;
        while (start + digits < stop && digits <= 3 && char.IsAsciiDigit(text[start + digits]))
        {
            digits++;
        }

        if (digits is >= 1 and <= 3 && start + digits + 1 < stop && text[start + digits] is '.' or ')'
            && (char.IsWhiteSpace(text[start + digits + 1]) || StartsWords(text[start + digits + 1])))
        {
            var from = Skip(text, start + digits + 1, stop);
            if (from < stop)
            {
                var number = int.Parse(text.AsSpan(start, digits), System.Globalization.CultureInfo.InvariantCulture);
                return new Line(TextBlockKind.Numbered, new TextItem(text[from..stop], number, from), whole);
            }
        }

        return new Line(TextBlockKind.Paragraph, whole, whole);
    }

    /// <summary>
    /// A marker typed without a space (owner, 2026-11-17: "-pirma", "1.pirma") counts only when words follow it: a letter or an
    /// opening quote or bracket. So "-5 %" keeps its minus, "1.5" and "10.30" stay numbers, "--" and "---" stay as typed. A
    /// "*" always needs its space, so "*žodis*" stays a word between stars.
    /// </summary>
    private static bool StartsWords(char next) => char.IsLetter(next) || next is '„' or '"' or '\'' or '(' or '«' or '“';

    private static int Skip(string text, int at, int stop)
    {
        while (at < stop && char.IsWhiteSpace(text[at]))
        {
            at++;
        }

        return at;
    }
}
