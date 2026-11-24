using System.Globalization;
using System.Text;

namespace Lt2.SharedKernel;

/// <summary>
/// The one folding of text for search (design 8.12.5): lower case in the Lithuanian culture, without diacritics ("Ąžuolaitis"
/// is "azuolaitis"), every run of white space one space. The bill search's stored copy and the words of a query are folded by
/// this, so they always meet in the same form.
/// </summary>
public static class SearchText
{
    private static readonly CultureInfo Lithuanian = CultureInfo.GetCultureInfo("lt-LT");

    /// <summary>The folded form of <paramref name="text"/>: lower case, no diacritics, single spaces, trimmed.</summary>
    public static string Fold(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var decomposed = text.ToLower(Lithuanian).Normalize(NormalizationForm.FormD);
        var folded = new StringBuilder(decomposed.Length);
        var pendingSpace = false;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                pendingSpace = folded.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                folded.Append(' ');
                pendingSpace = false;
            }

            folded.Append(c);
        }

        return folded.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>The words of a query: split on white space, no empty ones; none for a blank or missing query.</summary>
    public static IReadOnlyList<string> Words(string? query) =>
        string.IsNullOrWhiteSpace(query) ? [] : query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
