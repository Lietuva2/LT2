using Microsoft.Extensions.Configuration;

namespace Lt2.Identity.Application.Register;

/// <summary>
/// The trial switch of design 6.2, <c>Registration:WithoutEmail</c> (owner, 2026-11-27): on a trial site that sends no email, a
/// new address with a working invitation code is signed in at registration, with no code to type. It is deployment
/// configuration, not a runtime setting, so no administrator can switch it, and it is refused unless the deployment declares
/// that every email lands in a catcher (<c>Email:Caught</c>): a site whose mail goes out never runs with it on. Off by default.
/// </summary>
internal sealed class RegistrationWithoutEmail
{
    public const string Key = "Registration:WithoutEmail";

    private RegistrationWithoutEmail(bool on) => On = on;

    public bool On { get; }

    /// <summary>Reads the switch; throws when it is on where email is not caught, so such a site does not start.</summary>
    public static RegistrationWithoutEmail From(IConfiguration configuration)
    {
        var on = configuration.GetValue<bool>(Key);
        if (on && !configuration.GetValue<bool>("Email:Caught"))
        {
            throw new InvalidOperationException(
                $"{Key} is on but Email:Caught is not: registration without email is only for a trial site whose every email is caught.");
        }

        return new RegistrationWithoutEmail(on);
    }
}
