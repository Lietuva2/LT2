using Lt2.SharedKernel;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Mps;
using Lt2.Identity;
using Lt2.Web.Security;

namespace Lt2.Web.Services;

/// <summary>Hand-written mapping of the Identity module's MP link to the API contracts (design 8.5).</summary>
internal static class MpMapping
{
    public static MpLinkResponse Map(MpLinkState link) => new(link.Slug, link.FirstName, link.LastName, link.Part, link.MayWrite);

    public static MpOfferResponse Map(MpOffer offer) => new(offer.Slug, offer.FirstName, offer.LastName);

    public static MpOfficeResponse Map(MpOffice office) =>
        new(office.Members.Select(m => new MpOfficeMemberResponse(m.Id, m.DisplayName, m.Since)).ToList(), office.Limit);

    public static MpOfficeInvitationResponse Map(OfficeInvitation invitation) =>
        new(MpRoutes.OfficeInvitationPath(invitation.Token), invitation.ExpiresAt, invitation.Token, invitation.Emailed);
}

/// <summary>
/// <see cref="IMpApi"/> in-process, for components rendered on the server: the same module operations the endpoints call.
/// The take-over marks the front page's agenda out of date, as the endpoint does.
/// </summary>
internal sealed class ServerMpApi(
    IPoliticians politicians, FrontPageFreshness frontPage, IHttpContextAccessor accessor, IpHasher ipHasher, ErrorMapper errors, AnalyticsCopies analytics) : IMpApi
{
    public async Task<ApiResult> ClaimAsync(string slug, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await politicians.ClaimAsync(User, slug, IpHash, cancellationToken));

    public async Task<ApiResult<MpLinkResponse>> TakeOverAsync(MpTakeoverRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await politicians.TakeOverAsync(User, request.Token, request.Credential, IpHash, cancellationToken);
        if (result.IsSuccess)
        {
            frontPage.MarkStale();
        }

        return errors.ToApiResult(result, MpMapping.Map);
    }

    public async Task<ApiResult<Lt2.Api.Contracts.Auth.SignInResponse>> SignInWithRegistrationLinkAsync(MpRegistrationSignInRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var before = User.UserId();
        return errors.ToApiResult(await politicians.SignInWithRegistrationLinkAsync(request.Token, cancellationToken), outcome =>
        {
            // The other account's session ended as by signing out, and its kept analytics comparison with it (design 8.26.6).
            if (outcome.AccountChanged && before is { } previous)
            {
                analytics.Forget(previous);
            }

            return new Lt2.Api.Contracts.Auth.SignInResponse(outcome.AccountChanged);
        });
    }

    public async Task<ApiResult> EndAsync(CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await politicians.EndOwnAsync(User, IpHash, cancellationToken));

    public async Task<ApiResult<MpOfficeResponse>> GetOfficeAsync(CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await politicians.OfficeAsync(User, cancellationToken), MpMapping.Map);

    public async Task<ApiResult<MpOfficeInvitationResponse>> InviteOfficeMemberAsync(MpOfficeInvitationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return errors.ToApiResult(await politicians.InviteOfficeMemberAsync(User, request.Email, IpHash, cancellationToken), MpMapping.Map);
    }

    public async Task<ApiResult> RemoveOfficeMemberAsync(Guid memberId, CancellationToken cancellationToken = default) =>
        errors.ToApiResult(await politicians.RemoveOfficeMemberAsync(User, memberId, IpHash, cancellationToken));

    public async Task<ApiResult<MpLinkResponse>> AcceptOfficeInvitationAsync(MpOfficeAcceptanceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return errors.ToApiResult(await politicians.AcceptOfficeInvitationAsync(User, request.Token, request.NewPasskey, request.Passkey, IpHash, cancellationToken), MpMapping.Map);
    }

    private string? IpHash => accessor.HttpContext is { } context ? ipHasher.Hash(context) : null;

    private System.Security.Claims.ClaimsPrincipal User => accessor.HttpContext?.User ?? new System.Security.Claims.ClaimsPrincipal();
}
