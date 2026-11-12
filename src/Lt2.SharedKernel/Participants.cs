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
public sealed record ParticipantFacts(Guid UserId, string? DisplayName, string Email, DateTimeOffset MemberSince, bool HasPositionConsent);

/// <summary>Facts about members, for the Appeals module. Implemented by Identity.</summary>
public interface IParticipants
{
    /// <summary>A member whose registration is complete; <c>null</c> for an unknown or pending account.</summary>
    Task<ParticipantFacts?> FindAsync(Guid userId, CancellationToken cancellationToken);

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
}

/// <summary>A Seimas bill as the Appeals module refers to it: by its number, the natural key (design 3).</summary>
public sealed record BillFacts(string Number, string Title);

/// <summary>Looks up bills by number. Implemented by the Parliament module.</summary>
public interface IBillCatalogue
{
    Task<BillFacts?> FindAsync(string number, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, BillFacts>> FindManyAsync(IReadOnlyCollection<string> numbers, CancellationToken cancellationToken);
}
