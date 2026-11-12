using System.Data.Common;
using System.Text.Json;

namespace Lt2.SharedKernel;

/// <summary>One administrative or security-relevant action (design 5.5). Never carries emails or positions.</summary>
/// <param name="Action">Stable action name, for example <c>setting.changed</c>.</param>
/// <param name="ActorUserId">Who did it; <c>null</c> for the system.</param>
/// <param name="ActorRole">The role the actor acted in.</param>
/// <param name="Target">What it was done to, for example a setting key or a user id.</param>
/// <param name="Details">Optional structured details.</param>
/// <param name="IpHash">Keyed hash of the client IP address, never the address itself.</param>
public sealed record AuditEntry(
    string Action,
    Guid? ActorUserId,
    string? ActorRole,
    string? Target,
    JsonElement? Details = null,
    string? IpHash = null);

/// <summary>Appends to the audit log. Implemented by the Audit module; the application role may only insert.</summary>
public interface IAuditLog
{
    /// <summary>Writes the entry inside <paramref name="transaction"/> when given, otherwise on its own connection.</summary>
    Task WriteAsync(AuditEntry entry, DbTransaction? transaction = null, CancellationToken cancellationToken = default);
}
