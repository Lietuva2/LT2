using Lt2.Api.Contracts.Mps;
using Lt2.Identity;
using Lt2.SharedKernel;

namespace Lt2.Web.Services;

/// <summary>
/// The administrators' list of MPs whose profile is taken over (design 8.5.2): the MP's name and slug from the Parliament module
/// and since when from Identity, oldest first. Nothing of the account behind a link. One host-side join, as the public trace is.
/// </summary>
internal static class MpLinkList
{
    public static async Task<IReadOnlyList<AdminMpLinkResponse>> ReadAsync(IPoliticians politicians, IMpDirectory mps, CancellationToken cancellationToken)
    {
        var linked = await politicians.LinkedAsync(cancellationToken);
        var facts = await mps.MpsAsync(linked.Select(l => l.SeimasPersonId).ToList(), cancellationToken);
        return linked
            .Where(l => facts.ContainsKey(l.SeimasPersonId))
            .Select(l => new AdminMpLinkResponse(facts[l.SeimasPersonId].Slug, facts[l.SeimasPersonId].FirstName, facts[l.SeimasPersonId].LastName, l.Since))
            .ToList();
    }
}
