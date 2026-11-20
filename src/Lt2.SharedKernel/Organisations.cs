using System.Data.Common;

namespace Lt2.SharedKernel;

/// <summary>
/// An organisation as other modules refer to it (design 8.14.14, stage B): by its id, with what a reader is told. Never a
/// representative, never who registered it.
/// </summary>
/// <param name="ConfirmedBy"><c>domain</c>, <c>moderator</c> or null ("Nepatvirtinta").</param>
/// <param name="ConfirmedDomain">With <c>domain</c>: the domain shown as "@domenas".</param>
/// <param name="Status">One of <see cref="OrganisationStatuses"/>.</param>
public sealed record OrganisationFacts(Guid Id, string Slug, string Name, string? ConfirmedBy, string? ConfirmedDomain, string Status);

/// <summary>
/// A statement that is public now (design 8.14.14, stage B): what the follow emails tell. <paramref name="FirstPublicAt"/> is
/// when it first became public: when it was published, or, for one that waited, when a moderator published it. Edits do not
/// move it. Never its text and never who wrote it.
/// </summary>
public sealed record PublicStatementFacts(Guid Id, Guid OrganisationId, string BillNumber, string Stance, DateTimeOffset FirstPublicAt);

/// <summary>Organisations for the follows (design 8.14.14, stage B). Implemented by the Appeals module.</summary>
public interface IOrganisationDirectory
{
    /// <summary>The organisation with this profile address, whatever its status; null for an unknown one.</summary>
    Task<OrganisationFacts?> FindAsync(string slug, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, OrganisationFacts>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>
    /// The public statements of the given organisations that first became public at or after <paramref name="since"/>:
    /// published, of an active organisation. Hidden, waiting and withdrawn statements and those of blocked or closed
    /// organisations are not among them.
    /// </summary>
    Task<IReadOnlyList<PublicStatementFacts>> PublicStatementsAsync(IReadOnlyCollection<Guid> organisationIds, DateTimeOffset since, CancellationToken cancellationToken);
}

/// <summary>Whether an organisation's invitation codes may work (design 8.14.14, stage B): its state as the Appeals module knows it.</summary>
public static class OrganisationCodeStates
{
    public const string Active = "active";

    /// <summary>Blocked by a moderator: the codes stop working until the block is lifted.</summary>
    public const string Blocked = "blocked";

    /// <summary>Closed: the codes stop working for good.</summary>
    public const string Closed = "closed";
}

/// <summary>
/// Switches an organisation's invitation codes with the organisation (design 8.14.14, stage B): blocking switches them off,
/// lifting the block on again, closing off for good. An administrator's own switch-off is separate and stays. Implemented
/// by the Identity module, which owns invitations; called by Appeals in its own transaction, so the codes change with the
/// organisation or not at all.
/// </summary>
public interface IOrganisationCodeSwitch
{
    /// <param name="state">One of <see cref="OrganisationCodeStates"/>.</param>
    Task SetAsync(Guid organisationId, string state, DbTransaction transaction, CancellationToken cancellationToken);
}
