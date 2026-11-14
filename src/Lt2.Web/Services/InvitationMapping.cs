using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Me;
using Lt2.Appeals;
using Lt2.Identity;

namespace Lt2.Web.Services;

/// <summary>Invitation answers (design 6.2): the member's own code with its link, and the administrators' numbers.</summary>
internal static class InvitationMapping
{
    /// <summary>The registration form with the code filled in (a relative link; the page shows it in full).</summary>
    public static string Link(string code) => "/prisijungti?kodas=" + Uri.EscapeDataString(code);

    public static MyInvitationResponse Map(MyInvitation mine, IConfiguration configuration) =>
        new(mine.Code, Link(mine.Code), mine.Completed, mine.On, mine.On ? null : AppealMapping.Contact(configuration), mine.Usable);

    /// <summary>The administrators' numbers, organisations' codes labelled by the organisation's name (design 8.14.14, stage B).</summary>
    public static async Task<InvitationStatsResponse> MapAsync(InvitationStats s, IOrganisations organisations, CancellationToken ct)
    {
        var ids = s.Codes.Select(c => c.OrganisationId).OfType<Guid>().Distinct().ToList();
        var names = await organisations.NamesAsync(ids, ct);
        return new(
            s.From,
            s.To,
            s.Total,
            s.Days.Select(Map).ToList(),
            s.Codes.Select(c => new InvitationCodeResponse(
                c.Id, c.Kind, c.OrganisationId is { } org ? names.GetValueOrDefault(org) : c.Label, c.Completed, c.On, c.Replaced, c.Days.Select(Map).ToList(), c.OrganisationOff)).ToList(),
            s.Levels.Select(l => new InvitationLevelResponse(l.Level, l.Completed)).ToList(),
            s.WithoutCode);
    }

    private static InvitationDayResponse Map(InvitationDay d) => new(d.Day, d.Completed);
}
