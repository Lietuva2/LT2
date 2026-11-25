// One source file, compiled twice: into Lt2.SharedKernel for the modules (the Parliament module maps its enums to these) and,
// linked by Lt2.Api.Contracts.csproj with LT2_CONTRACTS defined, into the contracts the server, the browser and the tests
// share. The codes of the public parliament API (design 7.3, 7.4): lower-case English words, stable for the API.
#if LT2_CONTRACTS
namespace Lt2.Api.Contracts.Parliament;
#else
namespace Lt2.SharedKernel;
#endif

/// <summary>The kind of a Seimas body.</summary>
public static class BodyKinds
{
    public const string Faction = "faction";
    public const string Committee = "committee";
    public const string Commission = "commission";
    public const string Group = "group";
    public const string Other = "other";
}

/// <summary>The kind of a vote (design 7.2): what the rules over its wording found, or what a moderator corrected it to.</summary>
public static class VoteKinds
{
    /// <summary>The result of a bill's stage (presentation, consideration or adoption).</summary>
    public const string StageResult = "stage-result";

    public const string Amendment = "amendment";
    public const string Procedural = "procedural";

    /// <summary>What the rules cannot classify.</summary>
    public const string Other = "other";

    /// <summary>Every kind, in the order a moderator's form offers them (design 7.2).</summary>
    public static readonly IReadOnlyList<string> All = [StageResult, Amendment, Procedural, Other];
}

/// <summary>The stages of a bill in the Seimas, in their order; a planned date names one too (design 8.4).</summary>
public static class BillStages
{
    public const string Presentation = "presentation";
    public const string Consideration = "consideration";
    public const string Adoption = "adoption";

    public static readonly IReadOnlyList<string> All = [Presentation, Consideration, Adoption];
}

/// <summary>
/// Why a line of an entered agenda is refused (design 8.4, "Entering a day's agenda"): a line is <c>number | stage | title</c>,
/// and nothing of the day is applied while any line is refused.
/// </summary>
public static class AgendaEntryRefusals
{
    /// <summary>Not exactly three fields separated by <c>|</c>.</summary>
    public const string Fields = "fields";

    /// <summary>No bill's registration number in the first field.</summary>
    public const string Number = "number";

    public const string TitleEmpty = "title-empty";

    /// <summary>Longer than the rule's limit (2 000 characters).</summary>
    public const string TitleTooLong = "title-too-long";

    public static readonly IReadOnlyList<string> All = [Fields, Number, TitleEmpty, TitleTooLong];
}

/// <summary>What saving an entered day does to a bill listed on it (design 8.4); a bill taken off the day is listed apart.</summary>
public static class AgendaEntryChanges
{
    public const string Added = "added";
    public const string StageChanged = "stage-changed";
    public const string Unchanged = "unchanged";

    public static readonly IReadOnlyList<string> All = [Added, StageChanged, Unchanged];
}

/// <summary>An MP's choice in a vote. Not a member's side: a ballot may also abstain or be missing.</summary>
public static class BallotChoices
{
    public const string For = "for";
    public const string Against = "against";
    public const string Abstain = "abstain";
    public const string DidNotVote = "did-not-vote";
}

/// <summary>How a faction voted in one vote (design 7.3): its members' majority, a free vote, or nobody voting.</summary>
public static class FactionPositions
{
    public const string For = "for";
    public const string Against = "against";
    public const string Abstain = "abstain";
    public const string Free = "free";
    public const string NobodyVoted = "nobody-voted";
}
