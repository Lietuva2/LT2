// One source file, compiled twice: into Lt2.SharedKernel for the modules (the links in follow emails) and, linked by
// Lt2.Api.Contracts.csproj with LT2_CONTRACTS defined, into the contracts the server, the browser and the tests share (the
// member's page, the moderators' lists). Every link the site makes to a comment or an argument on a bill's page is made here.
using System.Globalization;

#if LT2_CONTRACTS
namespace Lt2.Api.Contracts.Comments;
#else
namespace Lt2.SharedKernel;
#endif

/// <summary>
/// A link that lands on one comment or one argument on its bill's page (design 8.23.6, 8.12.1; the designer's proposal of
/// 2026-11-17 awaiting the owner): the page's address carries <c>?komentaras={id}</c> or <c>?argumentas={id}</c>, which the
/// server resolves when it renders the page (the argument shown even beyond the first two of its side, the comment's
/// "Aptarimas" open with the comments through the linked one), and the fragment is the element's own id, so the browser
/// scrolls to it without script.
/// </summary>
public static class BillTextLinks
{
    /// <summary>The page's parameter naming the linked comment.</summary>
    public const string CommentParameter = "komentaras";

    /// <summary>The page's parameter naming the linked argument.</summary>
    public const string ReasonParameter = "argumentas";

    /// <summary>The comments a thread shows first (design 8.23.6: 10, then 20 a step, <see cref="ListPaging.TextStep"/>).</summary>
    public const int First = 10;

    /// <summary>The element id of a comment on the page: its stable anchor (design 8.23.6).</summary>
    public static string CommentElement(Guid commentId) => "comment-" + commentId.ToString("N", CultureInfo.InvariantCulture);

    /// <summary>The element id of an argument on the page.</summary>
    public static string ReasonElement(Guid reasonId) => "reason-" + reasonId.ToString("D", CultureInfo.InvariantCulture);

    /// <summary><c>/seimas/{number}?komentaras={id}#comment-{id}</c>.</summary>
    public static string ToComment(string billNumber, Guid commentId) =>
        Page(billNumber) + "?" + CommentParameter + "=" + commentId.ToString("N", CultureInfo.InvariantCulture) + "#" + CommentElement(commentId);

    /// <summary><c>/seimas/{number}?argumentas={id}#reason-{id}</c>.</summary>
    public static string ToReason(string billNumber, Guid reasonId) =>
        Page(billNumber) + "?" + ReasonParameter + "=" + reasonId.ToString("N", CultureInfo.InvariantCulture) + "#" + ReasonElement(reasonId);

    /// <summary>The element id of an organisation's statement in "Organizacijų pozicijos" on the bill's page (design 8.14.3).</summary>
    public static string StatementElement(Guid statementId) => "statement-" + statementId.ToString("D", CultureInfo.InvariantCulture);

    /// <summary>
    /// <c>/seimas/{number}#statement-{id}</c>: the statement on its bill's page (design 8.14.4, the summaries' line). Every
    /// statement shown is on the page, so the fragment alone finds it.
    /// </summary>
    public static string ToStatement(string billNumber, Guid statementId) => Page(billNumber) + "#" + StatementElement(statementId);

    /// <summary>The id an address parameter names; null for anything that is not one.</summary>
    public static Guid? Parse(string? value) =>
        !string.IsNullOrEmpty(value) && (Guid.TryParseExact(value, "N", out var id) || Guid.TryParseExact(value, "D", out id)) ? id : null;

    /// <summary>
    /// How many comments a thread shows so the linked one is among them, in context (design 8.23.6): everything from the first
    /// through the linked one, on the thread's own pages: the first 10, then steps of 20 (owner, 2026-11-19, 8.16.4). The 25th
    /// shows 30, the 31st 50.
    /// </summary>
    public static int ShownThrough(int position) => ListPaging.Through(position, First, ListPaging.TextStep);

    private static string Page(string billNumber) => "/seimas/" + Uri.EscapeDataString(billNumber);
}
