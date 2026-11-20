// One source file, compiled twice: into Lt2.SharedKernel for the modules (what they store and compare) and, linked by
// Lt2.Api.Contracts.csproj with LT2_CONTRACTS defined, into the contracts the server, the browser and the tests share. The
// codes of organisations and their statements (design 8.14): stored as text, carried by the API as they are.
#if LT2_CONTRACTS
namespace Lt2.Api.Contracts.Organisations;
#else
namespace Lt2.SharedKernel;
#endif

/// <summary>The status of an organisation (design 8.14).</summary>
public static class OrganisationStatuses
{
    public const string Active = "active";

    /// <summary>Blocked by a moderator: its statements are shown nowhere and its representatives cannot publish.</summary>
    public const string Blocked = "blocked";

    /// <summary>Closed by its last representative (or with that account): statements on open bills withdrawn.</summary>
    public const string Closed = "closed";
}

/// <summary>An organisation's stance on a bill (design 8.14.3): it asks to vote for, against, or makes remarks.</summary>
public static class StatementStances
{
    public const string For = "for";
    public const string Against = "against";
    public const string Remarks = "remarks";

    public static readonly IReadOnlyList<string> All = [For, Against, Remarks];

    /// <summary>The stances a statement may carry a goal with (design 8.14.5): asking to vote for or against.</summary>
    public static readonly IReadOnlyList<string> WithGoal = [For, Against];
}

/// <summary>The status of an organisation's statement (design 8.14).</summary>
public static class StatementStatuses
{
    /// <summary>Flagged by pre-screening: waits for a moderator, seen only by the organisation's representatives.</summary>
    public const string Pending = "pending";

    public const string Published = "published";

    /// <summary>Hidden by a moderator; "Atkurti" brings it back while the bill is open.</summary>
    public const string Hidden = "hidden";

    /// <summary>Withdrawn by a representative, or by closing the organisation; shown nowhere, a new one may follow.</summary>
    public const string Withdrawn = "withdrawn";

    /// <summary>The statuses that hold the organisation's one statement on a bill (a partial unique index).</summary>
    public static readonly IReadOnlyList<string> Holding = [Pending, Published, Hidden];
}
