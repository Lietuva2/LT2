// One source file, compiled twice: into Lt2.SharedKernel for the modules (what they store and compare) and, linked by
// Lt2.Api.Contracts.csproj with LT2_CONTRACTS defined, into the contracts the server, the browser and the tests share.
#if LT2_CONTRACTS
namespace Lt2.Api.Contracts.Digests;
#else
namespace Lt2.SharedKernel;
#endif

/// <summary>The statuses of a summary's run (design 8.19.1, owner 2026-11-17: no approval).</summary>
public static class DigestRunStatuses
{
    /// <summary>Prepared: its bills and arguments are chosen, moderators review it, it goes at its time.</summary>
    public const string Prepared = "prepared";

    /// <summary>Went while sending was off: made and published, never sent.</summary>
    public const string DryRun = "dry-run";

    public const string Sent = "sent";

    /// <summary>A moderator declined it before its time ("Nesiųsti", with a public statement): nothing went.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>At its time every bill it held was held by a moderator or no longer open: nothing went, nothing is public.</summary>
    public const string Dropped = "dropped";
}

/// <summary>
/// The summary a list row names in place of "riba pasiekta" for a bill whose threshold is reached (design 8.12.2, owner
/// 2026-11-21): the one planned that would hold it, or the last one really sent that held it.
/// </summary>
public static class RowSummaryKinds
{
    /// <summary>"suvestinė planuojama {day}": the day the next summary that would hold the bill is planned.</summary>
    public const string Planned = "planned";

    /// <summary>"suvestinė išsiųsta {day}": the day the last summary really sent to MPs that held the bill went.</summary>
    public const string Sent = "sent";
}
