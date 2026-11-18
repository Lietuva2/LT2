// One source file, compiled twice, as TextLayout.cs: into Lt2.SharedKernel and, linked by Lt2.Web.Client.csproj with
// LT2_CLIENT defined, into the browser's assembly, where TextChanges shows what it finds.
using System.Globalization;
using System.Text;

#if LT2_CLIENT
namespace Lt2.Web.Client;
#else
namespace Lt2.SharedKernel;
#endif

/// <summary>What happened to a piece of text between two versions.</summary>
public enum TextChange
{
    Same,
    Inserted,
    Removed,
}

/// <summary>A run of text that stayed, was inserted or was removed.</summary>
public sealed record TextPiece(TextChange Change, string Text);

/// <summary>
/// One paragraph or list item of the newer version with what changed in it, or one of the older version removed whole (all its
/// pieces <see cref="TextChange.Removed"/>; its kind and number are then the older version's).
/// </summary>
public sealed record ChangedItem(TextBlockKind Kind, int? Number, IReadOnlyList<TextPiece> Pieces);

/// <summary>
/// What changed between two versions of a typed text (owner, 2026-11-18): a word-level comparison for the public version
/// histories. Both texts are laid out first (<see cref="TextLayout"/>, design 8.2 "A text's layout"), so the result keeps the
/// paragraphs and lists of the newer version. Paragraphs and list items are matched whole (the longest common subsequence of
/// lines); between two matched ones, one line in place of one line, or an older and a newer line that share at least
/// <see cref="PairedShare"/> of their words, are compared word by word, and the rest are removed or inserted whole, so a moved
/// paragraph is removed where it was and inserted where it is. Within a line the tokens are words (letters and digits), runs of whitespace, and each other character on its
/// own, so nothing is lost: the same and removed pieces give the older text's line, the same and inserted the newer one's. The
/// work is capped at <see cref="MaxCells"/> comparisons; beyond it the changed part is shown replaced whole. A pure function.
/// </summary>
public static class TextDiff
{
    /// <summary>The most comparisons (cells of the comparison tables) one call makes before it falls back.</summary>
    public const int MaxCells = 2_000_000;

    /// <summary>The share of words (Dice coefficient) an older and a newer line need to be compared word by word.</summary>
    public const double PairedShare = 0.4;

    /// <summary>Lines that may be paired word by word in one stretch between unchanged lines; more are replaced whole.</summary>
    private const int MaxPairing = 10_000;

    /// <summary>The paragraphs and list items of <paramref name="newer"/> with their changes from <paramref name="older"/>.</summary>
    public static IReadOnlyList<ChangedItem> Of(string? older, string? newer)
    {
        var a = Items(older);
        var b = Items(newer);
        var budget = MaxCells;
        var result = new List<ChangedItem>();
        if ((long)a.Count * b.Count > budget)
        {
            Replace(result, a, b);
            return result;
        }

        budget -= a.Count * b.Count;

        // The longest common subsequence of whole lines, read from the end so the walk below goes forward.
        var lcs = new int[a.Count + 1, b.Count + 1];
        for (var i = a.Count - 1; i >= 0; i--)
        {
            for (var j = b.Count - 1; j >= 0; j--)
            {
                lcs[i, j] = string.Equals(a[i].Text, b[j].Text, StringComparison.Ordinal)
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        int x = 0, y = 0, fromA = 0, fromB = 0;
        while (x < a.Count && y < b.Count)
        {
            if (string.Equals(a[x].Text, b[y].Text, StringComparison.Ordinal))
            {
                Stretch(result, a[fromA..x], b[fromB..y], ref budget);
                result.Add(new ChangedItem(b[y].Kind, b[y].Number, [new TextPiece(TextChange.Same, b[y].Text)]));
                x++;
                y++;
                fromA = x;
                fromB = y;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                x++;
            }
            else
            {
                y++;
            }
        }

        Stretch(result, a[fromA..], b[fromB..], ref budget);
        return result;
    }

    /// <summary>The word-level changes from <paramref name="older"/> to <paramref name="newer"/>, two lines without their layout.</summary>
    public static IReadOnlyList<TextPiece> Words(string older, string newer)
    {
        var budget = MaxCells;
        return Words(older, newer, ref budget);
    }

    /// <summary>
    /// The tokens of a line: each run of letters and digits (a word, Lithuanian letters included), each run of whitespace,
    /// and each other character (punctuation) on its own. Joined, they give the line back.
    /// </summary>
    public static IReadOnlyList<string> Tokens(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokens = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            var j = i + 1;
            if (IsWordChar(text, i))
            {
                while (j < text.Length && (IsWordChar(text, j) || char.IsLowSurrogate(text[j])))
                {
                    j++;
                }
            }
            else if (char.IsWhiteSpace(text[i]))
            {
                while (j < text.Length && char.IsWhiteSpace(text[j]))
                {
                    j++;
                }
            }
            else if (char.IsHighSurrogate(text[i]) && j < text.Length && char.IsLowSurrogate(text[j]))
            {
                j++;
            }

            tokens.Add(text[i..j]);
            i = j;
        }

        return tokens;
    }

    private sealed record Line(TextBlockKind Kind, int? Number, string Text);

    private static List<Line> Items(string? text) =>
        TextLayout.Of(text ?? "").SelectMany(block => block.Items.Select(item => new Line(block.Kind, item.Number, item.Text))).ToList();

    private static void Replace(List<ChangedItem> result, IEnumerable<Line> removed, IEnumerable<Line> inserted)
    {
        result.AddRange(removed.Select(l => new ChangedItem(l.Kind, l.Number, [new TextPiece(TextChange.Removed, l.Text)])));
        result.AddRange(inserted.Select(l => new ChangedItem(l.Kind, l.Number, [new TextPiece(TextChange.Inserted, l.Text)])));
    }

    /// <summary>
    /// A stretch of older and newer lines between two unchanged ones: lines alike enough are paired, in order, so that the
    /// pairs share the most words; a paired line is compared word by word, the others are removed or inserted whole.
    /// </summary>
    private static void Stretch(List<ChangedItem> result, List<Line> a, List<Line> b, ref int budget)
    {
        if (a.Count == 0 || b.Count == 0 || a.Count * b.Count > MaxPairing)
        {
            Replace(result, a, b);
            return;
        }

        if (a.Count == 1 && b.Count == 1)
        {
            // One line in place of one line: the same paragraph, changed, however much.
            result.Add(new ChangedItem(b[0].Kind, b[0].Number, Words(a[0].Text, b[0].Text, ref budget)));
            return;
        }

        var wordsA = a.Select(l => WordCounts(l.Text)).ToList();
        var wordsB = b.Select(l => WordCounts(l.Text)).ToList();
        var score = new double[a.Count + 1, b.Count + 1];
        var share = new double[a.Count, b.Count];
        for (var i = a.Count - 1; i >= 0; i--)
        {
            for (var j = b.Count - 1; j >= 0; j--)
            {
                share[i, j] = Share(wordsA[i], wordsB[j]);
                var best = Math.Max(score[i + 1, j], score[i, j + 1]);
                if (share[i, j] >= PairedShare)
                {
                    best = Math.Max(best, share[i, j] + score[i + 1, j + 1]);
                }

                score[i, j] = best;
            }
        }

        int x = 0, y = 0;
        while (x < a.Count && y < b.Count)
        {
            if (share[x, y] >= PairedShare && score[x, y] == share[x, y] + score[x + 1, y + 1])
            {
                result.Add(new ChangedItem(b[y].Kind, b[y].Number, Words(a[x].Text, b[y].Text, ref budget)));
                x++;
                y++;
            }
            else if (score[x, y] == score[x + 1, y])
            {
                Replace(result, [a[x]], []);
                x++;
            }
            else
            {
                Replace(result, [], [b[y]]);
                y++;
            }
        }

        Replace(result, a[x..], b[y..]);
    }

    private static Dictionary<string, int> WordCounts(string text)
    {
        // For pairing only: "Prašome" and "prašome" are one word here; the comparison itself keeps the case.
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in Tokens(text))
        {
            if (IsWordChar(token, 0))
            {
                counts[token] = counts.GetValueOrDefault(token) + 1;
            }
        }

        return counts;
    }

    /// <summary>The Dice coefficient of two lines' words: twice the words in common over the words of both.</summary>
    private static double Share(Dictionary<string, int> a, Dictionary<string, int> b)
    {
        var total = a.Values.Sum() + b.Values.Sum();
        if (total == 0)
        {
            return 0;
        }

        var common = a.Sum(pair => Math.Min(pair.Value, b.GetValueOrDefault(pair.Key)));
        return 2.0 * common / total;
    }

    private static List<TextPiece> Words(string older, string newer, ref int budget)
    {
        var a = Tokens(older);
        var b = Tokens(newer);
        var prefix = 0;
        while (prefix < a.Count && prefix < b.Count && a[prefix] == b[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < a.Count - prefix && suffix < b.Count - prefix && a[a.Count - 1 - suffix] == b[b.Count - 1 - suffix])
        {
            suffix++;
        }

        var m = a.Count - prefix - suffix;
        var n = b.Count - prefix - suffix;
        var steps = new List<(TextChange Change, string Token)>(a.Count + b.Count);
        steps.AddRange(a.Take(prefix).Select(t => (TextChange.Same, t)));
        if ((long)m * n > budget)
        {
            // Too much to compare: the changed middle is shown replaced whole.
            steps.AddRange(a.Skip(prefix).Take(m).Select(t => (TextChange.Removed, t)));
            steps.AddRange(b.Skip(prefix).Take(n).Select(t => (TextChange.Inserted, t)));
        }
        else
        {
            budget -= m * n;
            Middle(steps, a, b, prefix, m, n);
        }

        steps.AddRange(a.Skip(prefix + m).Select(t => (TextChange.Same, t)));
        return Pieces(steps);
    }

    /// <summary>The longest common subsequence of the middle tokens, as steps in order (removed before inserted).</summary>
    private static void Middle(List<(TextChange, string)> steps, IReadOnlyList<string> a, IReadOnlyList<string> b, int from, int m, int n)
    {
        // Lengths fit 16 bits: m * n is at most MaxCells, so the shorter side is at most its square root.
        var width = n + 1;
        var lcs = new ushort[(m + 1) * width];
        for (var i = m - 1; i >= 0; i--)
        {
            for (var j = n - 1; j >= 0; j--)
            {
                lcs[i * width + j] = a[from + i] == b[from + j]
                    ? (ushort)(lcs[(i + 1) * width + j + 1] + 1)
                    : Math.Max(lcs[(i + 1) * width + j], lcs[i * width + j + 1]);
            }
        }

        int x = 0, y = 0;
        while (x < m && y < n)
        {
            if (a[from + x] == b[from + y])
            {
                steps.Add((TextChange.Same, a[from + x]));
                x++;
                y++;
            }
            else if (lcs[(x + 1) * width + y] >= lcs[x * width + y + 1])
            {
                steps.Add((TextChange.Removed, a[from + x]));
                x++;
            }
            else
            {
                steps.Add((TextChange.Inserted, b[from + y]));
                y++;
            }
        }

        for (; x < m; x++)
        {
            steps.Add((TextChange.Removed, a[from + x]));
        }

        for (; y < n; y++)
        {
            steps.Add((TextChange.Inserted, b[from + y]));
        }
    }

    /// <summary>
    /// Steps joined into pieces. A space left the same between two changes joins them, so "a b" replaced by "x y" reads as one
    /// removal and one insertion rather than word against word; within such a run all that was removed comes first.
    /// </summary>
    private static List<TextPiece> Pieces(List<(TextChange Change, string Token)> steps)
    {
        var pieces = new List<TextPiece>();
        var same = new StringBuilder();
        var removed = new StringBuilder();
        var inserted = new StringBuilder();
        var gap = new StringBuilder();
        var changing = false;

        void FlushChange()
        {
            if (removed.Length > 0)
            {
                pieces.Add(new TextPiece(TextChange.Removed, removed.ToString()));
            }

            if (inserted.Length > 0)
            {
                pieces.Add(new TextPiece(TextChange.Inserted, inserted.ToString()));
            }

            removed.Clear();
            inserted.Clear();
            same.Append(gap);
            gap.Clear();
            changing = false;
        }

        foreach (var (change, token) in steps)
        {
            if (change == TextChange.Same)
            {
                if (changing && string.IsNullOrWhiteSpace(token))
                {
                    gap.Append(token);
                    continue;
                }

                if (changing)
                {
                    FlushChange();
                }

                same.Append(token);
                continue;
            }

            if (!changing)
            {
                if (same.Length > 0)
                {
                    pieces.Add(new TextPiece(TextChange.Same, same.ToString()));
                    same.Clear();
                }

                changing = true;
            }
            else if (gap.Length > 0)
            {
                removed.Append(gap);
                inserted.Append(gap);
                gap.Clear();
            }

            (change == TextChange.Removed ? removed : inserted).Append(token);
        }

        if (changing)
        {
            FlushChange();
        }

        if (same.Length > 0)
        {
            pieces.Add(new TextPiece(TextChange.Same, same.ToString()));
        }

        return pieces;
    }

    private static bool IsWordChar(string text, int at)
    {
        var category = char.GetUnicodeCategory(text, at);
        return char.IsLetterOrDigit(text, at)
            || category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
    }
}
