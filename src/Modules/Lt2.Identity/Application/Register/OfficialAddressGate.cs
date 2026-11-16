using Lt2.SharedKernel;

namespace Lt2.Identity.Application.Register;

/// <summary>
/// Design 8.5.1 <b>[the designer's proposal, awaiting the owner]</b>: while registration needs an invitation, an MP's official
/// address may register without one, the mailbox being the proof, when <see cref="SettingKeys.RegistrationOfficialAddressesWithoutInvitation"/>
/// is on (off by default). Both invitation gates (registration and its completion) ask this one place.
/// </summary>
internal static class OfficialAddressGate
{
    public static async Task<bool> LetsThroughAsync(ISettings settings, IMpDirectory mps, string? email, CancellationToken cancellationToken) =>
        !string.IsNullOrWhiteSpace(email)
        && await settings.GetAsync(SettingKeys.RegistrationOfficialAddressesWithoutInvitation, cancellationToken)
        && await mps.OfficialAddressOwnerAsync(email, cancellationToken) is not null;
}
