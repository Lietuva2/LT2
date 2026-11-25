// One source file, compiled twice: into Lt2.SharedKernel for the modules (what they store and compare) and, linked by
// Lt2.Api.Contracts.csproj with LT2_CONTRACTS defined, into the contracts the server, the browser and the tests share. The
// codes of taking part on a bill (design 8.2, 8.12, 8.13): stored as text, carried by the API as they are.
#if LT2_CONTRACTS
namespace Lt2.Api.Contracts.Appeals;
#else
namespace Lt2.SharedKernel;
#endif

/// <summary>The two sides of a bill a member takes (design 8.2). Abstaining is not a request to an MP, so it is not offered.</summary>
public static class Sides
{
    public const string For = "for";
    public const string Against = "against";

    public static readonly IReadOnlyList<string> All = [For, Against];

    public static bool IsSide(string? value) => value is For or Against;
}

/// <summary>The status of a bill's record (design 8.1, 8.12.3).</summary>
public static class BillRecordStatuses
{
    /// <summary>People can take a side, write and mark arguments.</summary>
    public const string Open = "open";

    /// <summary>
    /// The Seimas finished the bill: the final vote, or a moderator recorded that it was rejected or withdrawn; the counts are
    /// frozen (design 8.1). Why is one of <see cref="ClosingReasons"/>.
    /// </summary>
    public const string Closed = "closed";

    /// <summary>
    /// Hidden by a moderator: taking part on the bill is closed; sides and arguments are kept unseen and come back with
    /// "Atkurti" (design 8.12.3).
    /// </summary>
    public const string Rejected = "rejected";
}

/// <summary>
/// Why a record closed (design 8.1 step 6, owner 2026-11-19): the Seimas's final vote (the closing job), or a moderator's record
/// that the Seimas rejected the bill or its initiators withdrew it, with the day and the source. Not to be confused with the
/// status <see cref="BillRecordStatuses.Rejected"/>, which is a record hidden by a moderator.
/// </summary>
public static class ClosingReasons
{
    public const string Rejected = "rejected";
    public const string Withdrawn = "withdrawn";

    /// <summary>A record closed by the Seimas's final vote; never a moderator's choice.</summary>
    public const string Vote = "vote";

    /// <summary>The reasons a moderator closes a record for (and may reopen it from).</summary>
    public static readonly IReadOnlyList<string> ByModerator = [Rejected, Withdrawn];
}

/// <summary>The status of an argument (design 8.2, 8.12.1).</summary>
public static class ReasonStatuses
{
    /// <summary>Flagged by pre-screening: waits for a moderator, visible to its author.</summary>
    public const string Pending = "pending";

    public const string Published = "published";
    public const string Hidden = "hidden";
    public const string Merged = "merged";
}

/// <summary>Who set the threshold that applies to a bill (design 8.12.2).</summary>
public static class ThresholdSetters
{
    /// <summary>The default threshold of all bills, <c>Digest:ThresholdDefault</c>, read when it is used.</summary>
    public const string Default = "default";

    public const string Moderator = "moderator";
}

/// <summary>
/// Who made a bill's description (design 5.3, 8.13): a moderator, a moderator starting from AI text, or the summary job
/// alone. An <see cref="AiDraft"/> description keeps its label "Sukurta DI · taisė žmonės" when a moderator corrects it
/// later; an <see cref="Ai"/> one becomes <see cref="AiDraft"/> once a moderator confirms or corrects it.
/// </summary>
public static class DescriptionSources
{
    public const string Moderator = "moderator";
    public const string AiDraft = "ai-draft";

    /// <summary>Made by the summary job from official text; no person has read it ("Sukurta DI · žmonių neperžiūrėta").</summary>
    public const string Ai = "ai";
}

/// <summary>
/// The two lists of the moderators' queue answer (design 8.3, the tabs Projektai and Argumentai):
/// <c>GET /api/v1/moderation/queue?list=</c> reads only that one; without it, both.
/// </summary>
public static class QueueLists
{
    public const string Records = "records";
    public const string Reasons = "reasons";
}

/// <summary>
/// What a report is about, and what a moderator's action and its notice to the reporters are about: a bill's description
/// (<see cref="Record"/>), an argument, an organisation or its statement (design 8.14.14), an MP's response (design 8.5.5), a
/// comment (design 8.23.8) or a member's public profile (design 8.25).
/// </summary>
public static class ReportTargets
{
    /// <summary>A bill's description; it may also be reported as inaccurate (design 8.13).</summary>
    public const string Record = "record";

    public const string Reason = "reason";
    public const string Organisation = "organisation";
    public const string Statement = "statement";
    public const string Response = "response";
    public const string Comment = "comment";

    /// <summary>A member's public profile, reported through the profile's own route, not the bills' one.</summary>
    public const string Profile = "profile";
}

/// <summary>Grounds for a report (design 8.3, Digital Services Act notice and action): the general ones and two that only some targets take.</summary>
public static class ReportGrounds
{
    public const string Illegal = "illegal";
    public const string PersonalData = "personal-data";
    public const string Insult = "insult";
    public const string Spam = "spam";
    public const string Other = "other";

    /// <summary>"Pranešti apie klaidą" (design 8.13): a bill's description only; an argument is its author's own opinion.</summary>
    public const string Inaccurate = "inaccurate";

    /// <summary>
    /// "Apsimeta kita organizacija" on an organisation or its statement (design 8.14.9); "Apsimeta kitu asmeniu ar pareigūnu" on
    /// a public profile (design 8.25.6).
    /// </summary>
    public const string Impersonation = "impersonation";

    /// <summary>The grounds every report may give.</summary>
    public static readonly IReadOnlyList<string> All = [Illegal, PersonalData, Insult, Spam, Other];

    /// <summary>A bill's description (<see cref="ReportTargets.Record"/>).</summary>
    public static readonly IReadOnlyList<string> Description = [Inaccurate, .. All];

    /// <summary>An organisation or its statement.</summary>
    public static readonly IReadOnlyList<string> Organisation = [Impersonation, .. All];

    /// <summary>A member's public profile (design 8.25.6), reported through the profile's own route.</summary>
    public static readonly IReadOnlyList<string> Profile = [Impersonation, .. All];

    /// <summary>The grounds a report on the bills' route about this target may give.</summary>
    public static IReadOnlyList<string> For(string targetKind) => targetKind switch
    {
        ReportTargets.Record => Description,
        ReportTargets.Organisation or ReportTargets.Statement => Organisation,
        _ => All,
    };
}
