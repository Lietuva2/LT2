namespace Lt2.Appeals.Domain;

/// <summary>
/// What is new to a member on a bill they follow (design 8.11.8), as pure functions: a text the bill's page shows is new when it
/// was made public, or changed, after the moment the member last opened the page; never a text of their own.
/// </summary>
internal static class NewsRules
{
    /// <summary>
    /// Whether a shown text is new to the member: made public (<paramref name="publicAt"/>) or changed (<paramref name="changedAt"/>,
    /// an organisation's or an MP's edited text) strictly after <paramref name="since"/>, and not the member's own.
    /// </summary>
    public static bool IsNew(DateTimeOffset publicAt, DateTimeOffset? changedAt, DateTimeOffset since, bool own) =>
        !own && (publicAt > since || changedAt > since);

    /// <summary>
    /// An organisation's statement is the member's own when they wrote it or represent the organisation now (its representatives
    /// write it together, 8.14.4).
    /// </summary>
    public static bool OwnStatement(Guid? writtenBy, Guid organisationId, Guid userId, IReadOnlySet<Guid> represented) =>
        writtenBy == userId || represented.Contains(organisationId);

    /// <summary>An MP's response is the member's own when they wrote it or speak for that MP (the MP's account or office, 8.5.3).</summary>
    public static bool OwnResponse(Guid? writtenBy, long seimasPersonId, Guid userId, long? speaksFor) =>
        writtenBy == userId || seimasPersonId == speaksFor;

    /// <summary>
    /// A comment is the member's own when they wrote it, or it speaks for an organisation they represent or for the MP they speak
    /// for (8.23.2: those voices are shared by the people behind them).
    /// </summary>
    public static bool OwnComment(Guid? author, Guid? organisationId, long? seimasPersonId, Guid userId, IReadOnlySet<Guid> represented, long? speaksFor) =>
        author == userId
        || (organisationId is { } o && represented.Contains(o))
        || (seimasPersonId is { } p && p == speaksFor);
}
