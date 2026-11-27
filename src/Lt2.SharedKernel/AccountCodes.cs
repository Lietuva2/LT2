// One source file, compiled twice: into Lt2.SharedKernel for the modules (what they store and compare) and, linked by
// Lt2.Api.Contracts.csproj with LT2_CONTRACTS defined, into the contracts the server, the browser and the tests share.
#if LT2_CONTRACTS
namespace Lt2.Api.Contracts.Me;
#else
namespace Lt2.SharedKernel;
#endif

/// <summary>How strongly the account's link to a real person was checked (design 6.2).</summary>
public static class VerificationLevels
{
    /// <summary>No code has been confirmed yet: a pending account, or one registered on a trial site without email (design 6.2).</summary>
    public const string None = "none";

    /// <summary>The person proved control of the email address.</summary>
    public const string Email = "email";
}
