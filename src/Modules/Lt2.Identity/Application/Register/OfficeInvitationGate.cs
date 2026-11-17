using Lt2.Identity.Domain;
using Lt2.Identity.Infrastructure;
using Lt2.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Lt2.Identity.Application.Register;

/// <summary>
/// Design 8.5.3 (owner, 2026-11-17: "office link should count as invitation"): while registration needs an invitation, a valid
/// MP office member's invitation lets a registration through in place of an invitation code. Valid: known, unused, unexpired,
/// and made by the account that is still the MP's. Both invitation gates (registration and its completion) ask this one
/// place; registering does not use the invitation up, accepting it does. Null when it does not let the registration through.
/// </summary>
internal static class OfficeInvitationGate
{
    /// <param name="Level">
    /// The level the person registers at: one below the MP's account (as a personal code of that account would bring them,
    /// design 6.2), kept on the profile alone, as an organisation's code is (8.14.14): nothing on the account names the MP.
    /// </param>
    internal sealed record Pass(int Level);

    public static async Task<Pass?> CheckAsync(IdentityDb db, string? code, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (OneTimeCode.Normalize(code) is null)
        {
            return null;
        }

        var hash = OneTimeCode.Hash(code);
        var token = await db.PoliticianTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (PoliticianRules.Check(token, PoliticianTokenKinds.Office, Guid.Empty, now) != PoliticianTokenCheck.Valid)
        {
            return null;
        }

        var mpAccount = await Politicians.PoliticianAccess.OpenLinks(db).AsNoTracking()
            .Where(l => l.SeimasPersonId == token!.SeimasPersonId && l.Part == PoliticianParts.Mp)
            .Select(l => (Guid?)l.UserId)
            .FirstOrDefaultAsync(cancellationToken);
        if (mpAccount is null || mpAccount != token!.UserId)
        {
            return null;
        }

        var mpLevel = await db.Profiles.AsNoTracking().Where(p => p.UserId == mpAccount).Select(p => p.InvitationLevel).FirstOrDefaultAsync(cancellationToken);
        return new Pass(InvitationRules.PersonalCodeLevel(InvitationRules.MemberLevel(mpLevel)));
    }
}
