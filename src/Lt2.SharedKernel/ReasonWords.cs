// One source file, compiled twice (owner, 2026-11-17): into Lt2.SharedKernel for the Appeals module, where pre-screening
// decides, and, linked by Lt2.Web.Client.csproj with LT2_CLIENT defined, into the browser's assembly for the writing form,
// which asks before publishing. The same count in both places (see TextExcerpt.cs for why the file is shared this way).
#if LT2_CLIENT
namespace Lt2.Web.Client;
#else
namespace Lt2.SharedKernel;
#endif

using System.Text.RegularExpressions;

/// <summary>
/// A too short argument (owner, 2026-11-17): fewer words than the setting <c>Appeals:MinReasonWords</c> (3; 0 = off). A word is a
/// run of letters or digits, so punctuation, emoji and spaces count for nothing. Arguments only: a short comment is normal.
/// </summary>
public static partial class ReasonWords
{
    /// <summary>How many words the text has: runs of letters or digits.</summary>
    public static int Count(string? text) => string.IsNullOrEmpty(text) ? 0 : Word().Count(text);

    /// <summary>Too short: the setting is on (above 0) and the text has fewer words than it.</summary>
    public static bool TooShort(string? text, int minWords) => minWords > 0 && Count(text) < minWords;

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex Word();
}
