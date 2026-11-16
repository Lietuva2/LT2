namespace Lt2.Appeals.Domain;

/// <summary>
/// <c>appeals.comment</c> (design 8.23.11): a member's public remark ("komentaras") under one argument, organisation's statement
/// or MP's response. One level, never edited; a deleted comment is a deleted row. <see cref="AuthorUserId"/> is the person,
/// or for a comment in an organisation's or an MP's name the account that wrote it: never public, emptied with the account.
/// Withheld from the read-only role.
/// </summary>
internal sealed class Comment
{
    public Guid Id { get; set; }

    /// <summary>See <see cref="CommentKinds"/>.</summary>
    public string TargetKind { get; set; } = "";

    public Guid TargetId { get; set; }

    /// <summary>The bill the target is on: for freezing with the bill and hiding with its record (a comment never creates one).</summary>
    public string BillNumber { get; set; } = "";

    /// <summary>See <see cref="CommentVoices"/>.</summary>
    public string SpeaksAs { get; set; } = CommentVoices.Person;

    /// <summary>With <see cref="CommentVoices.Organisation"/>: whose name the comment is shown under.</summary>
    public Guid? OrganisationId { get; set; }

    /// <summary>With <see cref="CommentVoices.Mp"/>: the MP, by the Seimas person number.</summary>
    public long? SeimasPersonId { get; set; }

    /// <summary>The person, or the writer behind an organisation's or an MP's comment; null once that account is deleted.</summary>
    public Guid? AuthorUserId { get; set; }

    public string Text { get; set; } = "";

    /// <summary>See <see cref="CommentStatuses"/>.</summary>
    public string Status { get; set; } = CommentStatuses.Published;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? HiddenAt { get; set; }
}

/// <summary>
/// <c>appeals.comment_agreement</c> (design 8.23.10): "Sutinku", public with the person's name, one per person per comment. Tells
/// no side, so it is readable by the read-only role as an argument is.
/// </summary>
internal sealed class CommentAgreement
{
    public Guid CommentId { get; set; }

    public Guid UserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>What a comment stands under (design 8.23.1): nothing else takes comments.</summary>
internal static class CommentKinds
{
    public const string Reason = "reason";
    public const string Statement = "statement";
    public const string Response = "response";

    public static readonly IReadOnlyList<string> All = [Reason, Statement, Response];
}

/// <summary>In whose name a comment is shown (design 8.23.2), decided by the server, never chosen in the form.</summary>
internal static class CommentVoices
{
    public const string Person = "person";
    public const string Organisation = "organisation";
    public const string Mp = "mp";
}

internal static class CommentStatuses
{
    public const string Published = "published";

    /// <summary>Hidden by a moderator: shown nowhere, nothing in its place; its "Sutinku" went with the hiding (design 8.23.8).</summary>
    public const string Hidden = "hidden";
}
