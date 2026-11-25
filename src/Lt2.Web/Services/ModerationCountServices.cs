using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Identity;

namespace Lt2.Web.Services;

/// <summary>
/// The counts of the moderators' tabs (design 8.3, owner 2026-11-25), composed from the modules' interfaces: each module
/// counts its lists by their own queries, with the same filter as each list (the same query builder), never by reading the
/// lists. Each count is the whole total its list states (the number its "Rodoma 100 iš N" uses). Read one after another,
/// never at once: the modules' reads and the staff check share one database context per request.
/// </summary>
internal sealed class ModerationCountReads(
    IAppealsModeration appeals, IOrganisationModeration organisations, IMpResponseModeration responses, ICommentModeration comments,
    IMemberProfiles profiles)
{
    public async Task<ModerationCountsResponse> CountsAsync(CancellationToken ct)
    {
        var appealCounts = await appeals.CountsAsync(ct);
        var organisationCounts = await organisations.CountsAsync(ct);
        var responseCounts = await responses.CountsAsync(ct);
        var commentCounts = await comments.CountsAsync(ct);
        var profileCounts = await profiles.ModerationCountsAsync(ct);

        var hiddenTotal = appealCounts.HiddenRecords + appealCounts.HiddenReasons
            + organisationCounts.Blocked + organisationCounts.HiddenStatements + organisationCounts.KeptOut
            + responseCounts.Hidden
            + profileCounts.Hidden
            + commentCounts.Hidden;
        return new ModerationCountsResponse(
            appealCounts.Records,
            appealCounts.Reasons,
            organisationCounts.Organisations,
            organisationCounts.Statements,
            responseCounts.Queue,
            profileCounts.Reported,
            commentCounts.Reported,
            appealCounts.Undescribed,
            hiddenTotal,
            appealCounts.Restrictions);
    }
}
