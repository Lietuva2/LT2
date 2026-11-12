using Lt2.Identity;
using Microsoft.AspNetCore.Authorization;

namespace Lt2.Web.Security;

/// <summary>
/// A staff role that works in this session (design 6.2): granted, activated, and the session signed in with the
/// account's staff passkey. Checked against the database on every request, so a reset takes effect at once. A session
/// signed in by email code never satisfies it, whatever roles the account has.
/// </summary>
internal sealed class StaffRoleRequirement(params string[] roles) : IAuthorizationRequirement
{
    public IReadOnlyList<string> Roles { get; } = roles;
}

internal sealed class StaffRoleHandler(IIdentityModule identity) : AuthorizationHandler<StaffRoleRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, StaffRoleRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var cancellationToken = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
        var session = await identity.GetStaffSessionAsync(context.User, cancellationToken);
        if (session.Has([.. requirement.Roles]))
        {
            context.Succeed(requirement);
        }
    }
}
