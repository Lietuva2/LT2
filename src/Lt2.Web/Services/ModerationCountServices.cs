using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Identity;

namespace Lt2.Web.Services;

/// <summary>
/// The counts of the moderators' tabs (design 8.3, owner 2026-11-25), composed from the modules' interfaces: each the
/// whole total its list states (the same number its "Rodoma 100 iš N" uses), never the items themselves. Read one after
/// another, never at once: the modules' reads and the staff check share one database context per request.
/// </summary>
internal sealed class ModerationCountReads(
    IAppealsModeration appeals, IOrganisationModeration organisations, IMpResponseModeration responses, ICommentModeration comments,
    IMemberProfiles profiles)
{
    public async Task<ModerationCountsResponse> CountsAsync(CancellationToken ct)
    {
        var queue = await appeals.QueueAsync(records: true, reasons: true, ct);
        var organisationQueue = await organisations.QueueAsync(ct);
        var responseItems = await responses.ItemsAsync(ct);
        var commentItems = await comments.ItemsAsync(ct);
        var profileItems = await profiles.QueueAsync(ct);
        var undescribed = await appeals.UndescribedAsync(ct);
        var hidden = await appeals.HiddenAsync(ct);
        var hiddenOrganisations = await organisations.HiddenAsync(ct);
        var restrictions = await appeals.RestrictionsAsync(ct);

        var hiddenTotal = hidden.RecordsTotal + hidden.ReasonsTotal
            + hiddenOrganisations.Organisations.Count + hiddenOrganisations.Statements.Count + hiddenOrganisations.KeptOut.Count
            + responseItems.HiddenTotal
            + profileItems.Count(p => p.HiddenAt is not null)
            + commentItems.HiddenTotal;
        return new ModerationCountsResponse(
            queue.RecordsTotal,
            queue.ReasonsTotal,
            organisationQueue.OrganisationsTotal,
            organisationQueue.StatementsTotal,
            responseItems.QueueTotal,
            profileItems.Count(p => p.Reports.Count > 0),
            commentItems.ReportedTotal,
            undescribed.Total,
            hiddenTotal,
            restrictions.Count);
    }
}
