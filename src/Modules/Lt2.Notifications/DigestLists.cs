using Lt2.Notifications.Digests;
using Lt2.SharedKernel;

namespace Lt2.Notifications;

/// <summary>
/// The summaries' list as other parts of the site show it (design 8.5.7, "Projektai, dėl kurių į jus kreipiamasi"): the bills
/// in summaries now, by the summaries' own rules and in their order, so the page and the email never disagree.
/// </summary>
public static class DigestLists
{
    /// <summary>The open records that reached their threshold and whose sending is not held, most relevant first (design 8.4).</summary>
    public static IReadOnlyList<DigestCandidate> InSummariesNow(IEnumerable<DigestCandidate> candidates) =>
        DigestRules.Order(candidates.Where(DigestRules.Eligible));
}
