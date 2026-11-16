namespace Lt2.SharedKernel;

/// <summary>
/// An MP's response as the follow email "Seimo narių atsakymai" tells it (design 8.20.1): the bill, the MP by the Seimas
/// person number, the stance now shown (<c>for</c>, <c>against</c>, <c>undecided</c>), and when that stance became public: the
/// first publication, or a later one that changed the stance. Never the explanation's text and never who wrote it.
/// </summary>
public sealed record PublicResponseFacts(string BillNumber, long SeimasPersonId, string Stance, DateTimeOffset AnnouncedAt);

/// <summary>The MPs' responses for the follow emails (design 8.20.1). Implemented by the Appeals module.</summary>
public interface IMpResponseFeed
{
    /// <summary>
    /// The responses that are public now (not withdrawn, not hidden, on a bill whose record the moderators have not hidden) and
    /// whose stance became public in [<paramref name="from"/>, <paramref name="to"/>). A changed explanation with the same
    /// stance does not move <see cref="PublicResponseFacts.AnnouncedAt"/>.
    /// </summary>
    Task<IReadOnlyList<PublicResponseFacts>> AnnouncedAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
