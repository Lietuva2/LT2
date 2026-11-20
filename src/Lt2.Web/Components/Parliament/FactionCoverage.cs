using Lt2.Api.Contracts.Parliament;

namespace Lt2.Web.Components.Parliament;

/// <summary>
/// "Balsavo kaip frakcija" counts only votes while the MP was in a faction (design 7.3, decided 2026-11-12). When those
/// periods do not cover the whole mandate (time in the mixed group or outside any faction), the profile names them.
/// </summary>
internal static class FactionCoverage
{
    /// <summary>The faction memberships within the mandate, when they leave part of it uncovered; otherwise none.</summary>
    public static IReadOnlyList<MembershipResponse> PeriodsWhenIncomplete(MandateResponse mandate, IEnumerable<MembershipResponse> memberships, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(mandate);
        var end = mandate.To is { } to && to < today ? to : today;
        var periods = memberships
            .Where(m => m.Body.Kind == BodyKinds.Faction && m.From <= end && (m.To is null || m.To >= mandate.From))
            .OrderBy(m => m.From)
            .ToList();
        if (periods.Count == 0)
        {
            return [];
        }

        var covered = mandate.From;
        foreach (var period in periods)
        {
            if (period.From > covered)
            {
                return periods; // a gap before this period
            }

            var last = period.To ?? end;
            if (last >= covered)
            {
                covered = last.AddDays(1);
            }
        }

        return covered <= end ? periods : [];
    }
}
