using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Identity;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Admin;

/// <summary>
/// Sensitive actions need a passkey confirmation within the last ten minutes (design 6.2): changing roles, resetting an
/// account and saving a runtime setting (design 6.9). The one check, for the endpoints and the in-process clients alike.
/// </summary>
internal static class FreshConfirmation
{
    public static readonly Error Required = Error.Forbidden("passkey.confirmation-required");

    /// <summary><see cref="Required"/> unless this session confirmed its staff passkey in the last ten minutes.</summary>
    public static async Task<Error?> MissingAsync(IIdentityModule identity, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        (await identity.GetStaffSessionAsync(user, cancellationToken)).RecentlyConfirmed ? null : Required;

    public static async Task<IResult?> RequireAsync(IIdentityModule identity, ClaimsPrincipal user, ErrorMapper errors, CancellationToken cancellationToken) =>
        await MissingAsync(identity, user, cancellationToken) is { } missing ? errors.ToResult(missing) : null;
}
