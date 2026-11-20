// One source file, compiled twice: into Lt2.SharedKernel for the modules (what they store and compare) and, linked by
// Lt2.Api.Contracts.csproj with LT2_CONTRACTS defined, into the contracts the server, the browser and the tests share. The
// codes of MPs' accounts and responses (design 8.5): stored as text, carried by the API as they are.
#if LT2_CONTRACTS
namespace Lt2.Api.Contracts.Mps;
#else
namespace Lt2.SharedKernel;
#endif

/// <summary>In which part an account speaks for an MP (design 8.5.4).</summary>
public static class PoliticianParts
{
    /// <summary>The MP's own account (design 8.5.2): one per person.</summary>
    public const string Mp = "mp";

    /// <summary>An office member (design 8.5.3): at most five per MP.</summary>
    public const string Office = "office";
}

/// <summary>An MP's stance (design 8.5.5): "balsuosiu už", "balsuosiu prieš", "dar neapsisprendžiau".</summary>
public static class ResponseStances
{
    public const string For = "for";
    public const string Against = "against";
    public const string Undecided = "undecided";

    public static readonly IReadOnlyList<string> All = [For, Against, Undecided];
}

/// <summary>The status of an MP's response (design 8.5.5).</summary>
public static class ResponseStatuses
{
    public const string Published = "published";

    /// <summary>Withdrawn by the MP's side: still shown, as "Atsakymas atsiimtas", with its versions (design 8.5.5).</summary>
    public const string Withdrawn = "withdrawn";

    /// <summary>Hidden by a moderator: its place shows the moderator's statement; "Atkurti" while the bill is open.</summary>
    public const string Hidden = "hidden";

    public static readonly IReadOnlyList<string> All = [Published, Withdrawn, Hidden];
}
