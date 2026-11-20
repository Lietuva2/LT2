// One source file, compiled twice: into Lt2.SharedKernel for the modules (what they store and compare) and, linked by
// Lt2.Api.Contracts.csproj with LT2_CONTRACTS defined, into the contracts the server, the browser and the tests share.
#if LT2_CONTRACTS
namespace Lt2.Api.Contracts.Feedback;
#else
namespace Lt2.SharedKernel;
#endif

/// <summary>What a member's feedback is about (design 8.10), chosen in the form or left empty.</summary>
public static class FeedbackKinds
{
    public const string Bug = "bug";
    public const string Idea = "idea";
    public const string Unclear = "unclear";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> All = [Bug, Idea, Unclear, Other];
}

/// <summary>Where the team is with an item of feedback (design 8.10).</summary>
public static class FeedbackStatuses
{
    public const string New = "new";
    public const string Seen = "seen";
    public const string Done = "done";

    public static readonly IReadOnlyList<string> All = [New, Seen, Done];
}
