namespace Lt2.Api.Contracts.Me;

/// <summary>
/// The caller's own invitation code (design 6.2): the code, the registration link with it filled in, how many people
/// completed registration with the caller's codes, and whether it is on. No names, dates or list. While an
/// administrator has it switched off, <c>Contact</c> is the moderators' address for questions.
/// </summary>
/// <param name="Usable">The code lets a registration through now (on and not used up); the share line shows a link only then (design 8.11.5).</param>
public sealed record MyInvitationResponse(string Code, string Link, int Completed, bool On, string? Contact, bool Usable = true);

/// <summary>Registrations completed on one Vilnius day.</summary>
public sealed record InvitationDayResponse(DateOnly Day, int Completed);

/// <summary>
/// One code for administrators; <c>Label</c> is a team code, a personal code's member's display name (null once deleted), or
/// an organisation's name (design 8.14.14, stage B). <c>On</c> is the administrator's switch; <c>OrganisationOff</c> is
/// <c>blocked</c> or <c>closed</c> while an organisation's code is off with its organisation.
/// </summary>
public sealed record InvitationCodeResponse(
    Guid Id, string Kind, string? Label, int Completed, bool On, bool Replaced, IReadOnlyList<InvitationDayResponse> Days, string? OrganisationOff = null);

public sealed record InvitationLevelResponse(int Level, int Completed);

/// <summary>
/// The administrators' numbers (design 6.2): completed registrations per day, the codes that brought the most people,
/// and per level. Numbers only: never who invited whom, never who used a code.
/// </summary>
public sealed record InvitationStatsResponse(
    DateOnly From,
    DateOnly To,
    int Total,
    IReadOnlyList<InvitationDayResponse> Days,
    IReadOnlyList<InvitationCodeResponse> Codes,
    IReadOnlyList<InvitationLevelResponse> Levels,
    int WithoutCode);
