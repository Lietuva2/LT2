// One source file, compiled twice: into Lt2.SharedKernel for the modules (what they store and compare) and, linked by
// Lt2.Api.Contracts.csproj with LT2_CONTRACTS defined, into the contracts the server, the browser and the tests share. The
// codes of comments (design 8.23): stored as text, carried by the API as they are.
#if LT2_CONTRACTS
namespace Lt2.Api.Contracts.Comments;
#else
namespace Lt2.SharedKernel;
#endif

/// <summary>What a comment stands under (design 8.23.1): nothing else takes comments.</summary>
public static class CommentKinds
{
    public const string Reason = "reason";
    public const string Statement = "statement";
    public const string Response = "response";

    public static readonly IReadOnlyList<string> All = [Reason, Statement, Response];
}

/// <summary>In whose name a comment is shown (design 8.23.2), decided by the server, never chosen in the form.</summary>
public static class CommentVoices
{
    public const string Person = "person";
    public const string Organisation = "organisation";
    public const string Mp = "mp";
}

/// <summary>The status of a comment (design 8.23.8).</summary>
public static class CommentStatuses
{
    public const string Published = "published";

    /// <summary>Hidden by a moderator: shown nowhere, nothing in its place; its "Sutinku" went with the hiding (design 8.23.8).</summary>
    public const string Hidden = "hidden";
}
