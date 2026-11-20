// One source file, compiled twice: into Lt2.SharedKernel for the modules (finding the author of an item) and, linked by
// Lt2.Api.Contracts.csproj with LT2_CONTRACTS defined, into the contracts the server, the browser and the tests share.
#if LT2_CONTRACTS
namespace Lt2.Api.Contracts.Admin;
#else
namespace Lt2.SharedKernel;
#endif

/// <summary>The kinds of item an administrator locks the author of (design 8.3): the same places the writing restriction is reached from, and a public profile.</summary>
public static class LockTargets
{
    public const string Reason = "reason";
    public const string Comment = "comment";
    public const string Statement = "statement";
    public const string Profile = "profile";

    public static readonly IReadOnlyList<string> All = [Reason, Comment, Statement, Profile];
}
