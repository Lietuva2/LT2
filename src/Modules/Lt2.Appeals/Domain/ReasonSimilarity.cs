using System.Globalization;
using System.Text;

namespace Lt2.Appeals.Domain;

/// <summary>
/// "This argument looks like an existing one" (design 1.3, 8.1): plain text similarity within one bill and side, with
/// nothing external. Two measures, the higher counts: shared word stems (the first five letters, which absorbs most
/// Lithuanian endings) and shared letter trigrams (which absorbs typos and small rewordings).
/// </summary>
internal static class ReasonSimilarity
{
    /// <summary>At or above this score an existing argument is offered before a new one is written.</summary>
    public const double Threshold = 0.5;

    /// <summary>At most this many similar arguments are offered.</summary>
    public const int MaxOffered = 3;

    private const int StemLength = 5;

    public static double Score(string a, string b)
    {
        var left = Fold(a);
        var right = Fold(b);
        if (left.Length == 0 || right.Length == 0)
        {
            return 0;
        }

        if (left == right)
        {
            return 1;
        }

        return Math.Max(Dice(Stems(left), Stems(right)), Dice(Trigrams(left), Trigrams(right)));
    }

    /// <summary>The existing arguments most like <paramref name="text"/>, best first, those at or above the threshold only.</summary>
    public static IReadOnlyList<T> MostSimilar<T>(string text, IEnumerable<T> existing, Func<T, string> textOf) =>
        existing
            .Select(e => (Item: e, Score: Score(text, textOf(e))))
            .Where(x => x.Score >= Threshold)
            .OrderByDescending(x => x.Score)
            .Take(MaxOffered)
            .Select(x => x.Item)
            .ToList();

    /// <summary>Lower case, Lithuanian letters folded to Latin ones, everything but letters and digits made a space.</summary>
    internal static string Fold(string text)
    {
        var decomposed = text.ToLower(CultureInfo.InvariantCulture).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static HashSet<string> Stems(string folded) =>
        folded.Split(' ')
            .Where(w => w.Length >= 3)
            .Select(w => w.Length > StemLength ? w[..StemLength] : w)
            .ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> Trigrams(string folded)
    {
        var padded = " " + folded + " ";
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i + 3 <= padded.Length; i++)
        {
            set.Add(padded.Substring(i, 3));
        }

        return set;
    }

    private static double Dice(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0)
        {
            return 0;
        }

        var shared = a.Count(b.Contains);
        return 2.0 * shared / (a.Count + b.Count);
    }
}
