// One source file, compiled twice: into Lt2.SharedKernel for the modules (the API's pages of arguments and comments) and,
// linked by Lt2.Api.Contracts.csproj with LT2_CONTRACTS defined, into the contracts the server, the browser and the tests
// share (every "Rodyti daugiau" of the pages).
#if LT2_CONTRACTS
namespace Lt2.Api.Contracts;
#else
namespace Lt2.SharedKernel;
#endif

/// <summary>
/// How a long list grows by its one "show more" (design 8.16.4; owner, 2026-11-19): the first page stays as small as each list
/// says, light for most readers, and every further page is larger, since whoever presses wants more. A list of rows (the
/// agenda's and the front page's days, <c>/projektai</c>, the history of votes, a vote's ballots, an MP's votes, a member's
/// texts, <c>/statistika</c>'s bills) adds <see cref="RowStep"/> a press; a list of members' long texts (the arguments, the
/// comments, the MPs' responses) adds <see cref="TextStep"/>. The numbers are the design's, not settings.
/// </summary>
public static class ListPaging
{
    /// <summary>The rows "Rodyti daugiau" adds under a list of rows.</summary>
    public const int RowStep = 50;

    /// <summary>The texts "Rodyti daugiau …" adds under a list of long texts.</summary>
    public const int TextStep = 20;

    /// <summary>What "Rodyti daugiau" asks for: the <paramref name="shown"/> and a <paramref name="step"/> more, never beyond <paramref name="cap"/>.</summary>
    public static int Next(int shown, int step, int cap) => Math.Min(shown + step, cap);

    /// <summary>
    /// How many a list shows so that the item at <paramref name="position"/> (1-based) is among them, on the list's own pages:
    /// the first page (<paramref name="first"/>) when it holds it, else the first page and as many steps as it takes. With 10
    /// first and 20 a step, the 25th shows 30 and the 31st 50.
    /// </summary>
    public static int Through(int position, int first, int step) =>
        position <= first ? first : first + ((position - first + step - 1) / step * step);
}
