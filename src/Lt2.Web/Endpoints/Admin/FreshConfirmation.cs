using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Identity;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Admin;

/// <summary>Sensitive actions need a passkey confirmation within the last ten minutes (design 6.2).</summary>
internal static class FreshConfirmation
{
    public static async Task<IResult?> RequireAsync(IIdentityModule identity, ClaimsPrincipal user, ErrorMapper errors, CancellationToken cancellationToken)
    {
        var session = await identity.GetStaffSessionAsync(user, cancellationToken);
        return session.RecentlyConfirmed ? null : errors.ToResult(Error.Forbidden("passkey.confirmation-required"));
    }
}
