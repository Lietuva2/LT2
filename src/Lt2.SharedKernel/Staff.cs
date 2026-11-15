using System.Security.Claims;

namespace Lt2.SharedKernel;

/// <summary>The signed-in account as every module and host reads it from the principal.</summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>The account's id from the name-identifier claim; null when nobody is signed in or the claim is not an id.</summary>
    public static Guid? UserId(this ClaimsPrincipal? principal) =>
        Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}

/// <summary>A moderator or administrator acting in a staff-passkey session, for the audit log and statements of reasons.</summary>
/// <param name="Role">The role acted in, for example <c>moderator</c>.</param>
/// <param name="IpHash">Keyed hash of the client address, never the address.</param>
public sealed record StaffActor(Guid UserId, string Role, string? IpHash);
