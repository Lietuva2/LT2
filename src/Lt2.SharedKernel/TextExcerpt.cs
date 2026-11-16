// One source file, compiled twice (owner, 2026-11-16): into Lt2.SharedKernel for the modules (the MPs' email and its
// preview, Notifications) and, linked by Lt2.Web.Client.csproj with LT2_CLIENT defined, into the browser's assembly for
// the pages. The browser may not depend on SharedKernel and the modules may not depend on the contracts
// (tests/Lt2.ArchitectureTests/ModuleBoundaryTests.cs), so the one definition is this file, in a namespace of each.
#if LT2_CLIENT
namespace Lt2.Web.Client;
#else
namespace Lt2.SharedKernel;
#endif

/// <summary>A member's text as a list or an email shows it: the beginning, and the rest when there is one.</summary>
/// <param name="Shown">What is shown at first; the whole text when <paramref name="Rest"/> is null.</param>
/// <param name="Rest">What follows (behind "Rodyti daugiau" on a page, behind a link in an email); null when shown whole.</param>
public sealed record TextExcerpt(string Shown, string? Rest)
{
    /// <summary>An argument: about this many characters are shown at first (design 8.2, owner 2026-11-16).</summary>
    public const int ArgumentShown = 300;

    /// <summary>An argument at most this much longer than <see cref="ArgumentShown"/> is shown whole.</summary>
    public const int ArgumentMargin = 100;

    /// <summary>
    /// An organisation's statement or an MP's response: they were shown whole up to 1 500 characters and should not be cut
    /// short (design 8.5.6, 8.14.3, owner 2026-11-16).
    /// </summary>
    public const int StatementShown = 1500;

    /// <summary>With the default length setting (2 000) a statement or a response is never cut.</summary>
    public const int StatementMargin = 500;

    /// <summary>
    /// Splits a text: one no longer than <paramref name="shown"/> + <paramref name="margin"/> stays whole; a longer one is cut
    /// at the last white space within the first <paramref name="shown"/> characters (a hard cut only when a single word
    /// fills them), so nothing is lost: the shown part and the rest, joined at the white space, are the text. A pure
    /// function, the same on the server, in the browser and in the email.
    /// </summary>
    public static TextExcerpt Of(string text, int shown = ArgumentShown, int margin = ArgumentMargin)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(shown);
        ArgumentOutOfRangeException.ThrowIfNegative(margin);
        if (text.Length <= shown + margin)
        {
            return new TextExcerpt(text, null);
        }

        var cut = shown;
        while (cut > 0 && !char.IsWhiteSpace(text[cut]))
        {
            cut--;
        }

        if (cut == 0)
        {
            cut = shown;
        }

        var start = text[..cut].TrimEnd();
        var rest = text[cut..].TrimStart();
        return rest.Length == 0 ? new TextExcerpt(text, null) : new TextExcerpt(start, rest);
    }
}

/// <summary>The length the setting <c>Texts:MaxLength</c> allows now (design 5.6): the setting, or the ceiling when it is 0.</summary>
public static class MemberTextLength
{
    /// <summary>
    /// The technical ceiling on a member's text: a guard against a runaway request, not a rule. <c>SettingKeys.MemberTextCeiling</c>
    /// is this; <c>MemberTextLimits.Ceiling</c> in the contracts is the same number (a unit test).
    /// </summary>
    public const int Ceiling = 20_000;

    public static int Allowed(int setting) => setting <= 0 ? Ceiling : Math.Min(setting, Ceiling);
}
