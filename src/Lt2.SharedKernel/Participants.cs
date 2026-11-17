using System.Data.Common;

namespace Lt2.SharedKernel;

/// <summary>
/// What the Appeals module needs to know about a member: implemented by the Identity module, which owns accounts and
/// consents (design 5.2). Never carries a position.
/// </summary>
/// <param name="UserId">The account.</param>
/// <param name="DisplayName">The public name, or <c>null</c> if the person gave none.</param>
/// <param name="Email">The address on the account, for notices to the author (moderation, design 8.3).</param>
/// <param name="MemberSince">When the registration was completed; the "accounts older than 30 days" count uses it.</param>
/// <param name="HasPositionConsent">The explicit consent to store positions (design 6.5) is given and not withdrawn.</param>
/// <param name="IsMpAccount">
/// The account is an MP's own linked account (design 8.5.4: the MP's part of a <c>politician_link</c>, not an office
/// member's), read from the database on every call, so ending a link works at once. See <see cref="MpAccounts"/>.
/// </param>
public sealed record ParticipantFacts(Guid UserId, string? DisplayName, string Email, DateTimeOffset MemberSince, bool HasPositionConsent, bool IsMpAccount = false);

/// <summary>
/// Design 8.5.4 <b>[decided by the owner, 2026-11-17]</b>: the MP's own linked account takes no part as a
/// participant (no side, no argument, no "Man svarbu", no party-support declaration, no organisation); the MP speaks through
/// the response. Office members are not restricted. This is the one place the rule is said: every action that checks its
/// participant refuses through <see cref="Refusal"/>. The islands do not show the forms it would refuse (design 8.5.4);
/// this refusal stays as the safety net.
/// </summary>
public static class MpAccounts
{
    public static readonly Error TakesNoPart = Error.Forbidden("mp.takes-no-part");

    /// <summary><see cref="TakesNoPart"/> for an MP's own account; null for anyone else (an unknown account included).</summary>
    public static Error? Refusal(ParticipantFacts? participant) => participant is { IsMpAccount: true } ? TakesNoPart : null;
}

/// <summary>
/// Design 8.5.5: where a notice about an MP's response goes. Implemented by Identity, which owns the links; nothing else of
/// a link leaves it (who the office members are, who wrote a response).
/// </summary>
public interface IMpLinks
{
    /// <summary>
    /// The own address of the account linked as the MP <paramref name="seimasPersonId"/> (the official address when the profile
    /// was taken over), or null when no account is linked as that MP now. Used only to write an outbox row; never logged.
    /// </summary>
    Task<string?> MpAccountAddressAsync(long seimasPersonId, CancellationToken cancellationToken);

    /// <summary>
    /// For each of <paramref name="userIds"/> linked now to an MP, as the MP's own account or an office member (design 8.5.2,
    /// 8.5.3), that MP's Seimas person number; accounts without a link are missing. Used only to leave an MP's own responses out
    /// of the follow email to the MP's account and office (design 8.20.1); never shown, logged or combined with anything else.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, long>> SpeaksForAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);
}

/// <summary>Facts about members, for the Appeals module. Implemented by Identity.</summary>
public interface IParticipants
{
    /// <summary>A member whose registration is complete; <c>null</c> for an unknown or pending account.</summary>
    Task<ParticipantFacts?> FindAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The members among <paramref name="userIds"/> whose registration is complete, in one query; others are missing.</summary>
    Task<IReadOnlyDictionary<Guid, ParticipantFacts>> FindManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>Public display names (or <c>null</c> where none was given) of the given accounts that exist.</summary>
    Task<IReadOnlyDictionary<Guid, string?>> DisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);
}

/// <summary>The explicit consent a person gives before their first position (design 6.5).</summary>
public static class PositionConsent
{
    public const string Kind = "positions";

    /// <summary>
    /// The version of the consent text in force. The text itself is in the client's resources under this version; a new
    /// wording gets a new version, and consents given to an older one are asked again.
    /// </summary>
    public const string TextVersion = "2026-11-13";
}

/// <summary>
/// Deletes a person's positions and their "Man svarbu" marks, inside the caller's transaction: withdrawing the consent
/// deletes the positions (design 6.5), and so will deleting the account. Implemented by the Appeals module.
/// </summary>
public interface IPositionErasure
{
    /// <summary>Returns how many positions were deleted.</summary>
    Task<int> EraseAsync(Guid userId, DbTransaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// Withdraws the person's sides (with their "Man svarbu" marks) on every bill whose record is not closed, as sides
    /// withdrawn by hand: the counts go down (design 8.5.2, the take-over). A closed record's counts are frozen and keep its
    /// sides. Returns how many sides were withdrawn.
    /// </summary>
    Task<int> WithdrawOpenSidesAsync(Guid userId, DbTransaction transaction, CancellationToken cancellationToken);
}

/// <summary>
/// Whether a moderator's writing restriction (design 8.3) holds for a person now, for the modules that write text others see
/// outside Appeals (design 6.2: changing the display name). Implemented by the Appeals module, which keeps the restrictions.
/// </summary>
public interface IWritingRestrictions
{
    Task<bool> IsRestrictedAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// What one module holds of one person (design 6.5): exported on request, erased with the account. Implemented by Appeals
/// (positions, marks, arguments, appeals opened before 2026-11-14, reports, flags, draft requests) and by Notifications (follows and
/// the follow-email switches, design 8.11.2). The export holds only the person's own data.
/// </summary>
public interface IAccountData
{
    /// <summary>The key of this module's part in the export, for example <c>appeals</c>.</summary>
    string Section { get; }

    /// <summary>The person's data in this module, as plain data.</summary>
    Task<object> ExportAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the person's data in this module in the caller's transaction. Appeals the person opened stay, without a
    /// name. Counts fall; frozen summary snapshots are not touched.
    /// </summary>
    Task EraseAsync(Guid userId, DbTransaction transaction, CancellationToken cancellationToken);
}

/// <summary>A Seimas bill as the Appeals module refers to it: by its number, the natural key (design 3).</summary>
public sealed record BillFacts(string Number, string Title);

/// <summary>Looks up bills by number. Implemented by the Parliament module.</summary>
public interface IBillCatalogue
{
    Task<BillFacts?> FindAsync(string number, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, BillFacts>> FindManyAsync(IReadOnlyCollection<string> numbers, CancellationToken cancellationToken);

    /// <summary>
    /// Every active bill (design 8.12.1): in the catalogue and not finished, that is without a final vote
    /// (<see cref="FinalVoteAsync"/>), by number.
    /// </summary>
    Task<IReadOnlyList<BillFacts>> ActiveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The Seimas's final vote on the bill: the latest stage result at the adoption stage, or <c>null</c> while there is
    /// none. Rejection or withdrawal at an earlier stage is not in the open data (see design 5.1, bill status).
    /// </summary>
    Task<BillFinalVote?> FinalVoteAsync(string number, CancellationToken cancellationToken);

    /// <summary>The stage-result votes on the given bills held at or after <paramref name="since"/>, oldest first (design 8.11.2).</summary>
    Task<IReadOnlyList<StageVoteFacts>> StageResultsAsync(IReadOnlyCollection<string> numbers, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Each given bill's latest stage-result vote, by bill number; a bill without one is missing.</summary>
    Task<IReadOnlyDictionary<string, StageVoteFacts>> LatestStageResultsAsync(IReadOnlyCollection<string> numbers, CancellationToken cancellationToken);

    /// <summary>
    /// How the given MPs (Seimas person ids) voted in one vote: <c>for</c>, <c>against</c>, <c>abstain</c> or
    /// <c>did-not-vote</c>; an MP without a ballot is missing. Public data about a public office.
    /// </summary>
    Task<IReadOnlyDictionary<long, string>> ChoicesAsync(long voteId, IReadOnlyCollection<long> seimasPersonIds, CancellationToken cancellationToken);
}

/// <summary>
/// A stage-result vote on a bill (design 8.11.2): what the follow emails tell. <c>HasResult</c>: per-MP votes were read
/// (never for a vote approved by consensus).
/// </summary>
public sealed record StageVoteFacts(
    long VoteId, string BillNumber, string? Stage, DateTimeOffset HeldAt, bool IsConsensus, bool HasResult, int? For, int? Against, int? Abstain, string? OfficialNote);

/// <summary>An MP as a follow refers to them: by the Seimas person id, stable across terms (design 8.11.2).</summary>
public sealed record MpFacts(long SeimasPersonId, string Slug, string FirstName, string LastName);

/// <summary>The vote that closes a record (design 8.1).</summary>
public sealed record BillFinalVote(long VoteId, DateTimeOffset HeldAt);

/// <summary>
/// A bill's planned date on the Seimas agenda (design 5.1 planned_item, 8.4): from the lrs.lt agenda pages, or entered
/// by a moderator, whose row takes precedence. Shown as "Planuojama …", never as a countdown.
/// </summary>
/// <param name="Stage"><c>presentation</c>, <c>consideration</c>, <c>adoption</c>, or <c>null</c>.</param>
/// <param name="SetByModerator">A moderator entered or corrected it.</param>
public sealed record PlannedDate(string BillNumber, DateOnly Date, string? Stage, string? SourceUrl, bool SetByModerator);

/// <summary>Planned dates of bills. Implemented by the Parliament module.</summary>
public interface IPlannedAgenda
{
    /// <summary>The current planned date of each given bill that has one today or later.</summary>
    Task<IReadOnlyDictionary<string, PlannedDate>> ForBillsAsync(IReadOnlyCollection<string> numbers, CancellationToken cancellationToken);

    /// <summary>Every current planned item with a date in [<paramref name="from"/>, <paramref name="to"/>].</summary>
    Task<IReadOnlyList<PlannedDate>> BetweenAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>
    /// A moderator's correction (design 8.4), written in the caller's transaction: it replaces the bill's moderator row
    /// and takes precedence over the agenda pages.
    /// </summary>
    Task CorrectAsync(string number, DateOnly date, string? stage, Guid moderatorUserId, string note, DbTransaction transaction, CancellationToken cancellationToken);
}