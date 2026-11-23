using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;

namespace Lt2.Web.Services;

/// <summary>
/// <c>GET /api/v1/appeals</c> (design 8.12.5), composed by the host: the list as the Appeals module gives it, and what the row
/// shared with the agenda and the front page needs besides (owner, 2026-11-23): for a bill whose threshold is reached the summary
/// it names (<see cref="RowSummaries"/>, design 8.12.2), and the organisations with a public statement on each bill shown, composed
/// as the agenda does (<see cref="AgendaReads.OrganisationsOf"/>). The stage is the bill's planned item's, which the Appeals module
/// already gives. Reads nothing about the caller, so the answer is public and the same for everyone.
/// </summary>
internal sealed class BillListReads(IAppealsModule appeals, IOrganisations organisations, RowSummaries summaries)
{
    public async Task<BillListResponse> ListAsync(BillListQuery query, CancellationToken ct)
    {
        var page = AppealMapping.Map(await appeals.ListAsync(query, ct));
        var summaryOf = await summaries.OfAsync(page.Items.Select(i => (i.Id, i.Status, i.Threshold.Reached)), ct);
        var shown = await organisations.ShownOnBillsAsync(page.Items.Select(i => i.Bill.Number).ToList(), ct);
        return page with
        {
            Items = page.Items
                .Select(i => i with { Summary = i.Id is { } id ? summaryOf.GetValueOrDefault(id) : null, Organisations = AgendaReads.OrganisationsOf(shown.GetValueOrDefault(i.Bill.Number)) })
                .ToList(),
        };
    }
}
