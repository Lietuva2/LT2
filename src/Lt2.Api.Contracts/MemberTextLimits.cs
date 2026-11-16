namespace Lt2.Api.Contracts;

/// <summary>
/// The length of what members write for others to read: arguments, organisations' statements, MPs' responses (design 5.6,
/// 8.2, owner 2026-11-16). The rule is the runtime setting <c>Texts:MaxLength</c> (0 = no limit), which the forms read from
/// <see cref="Appeals.AppealRulesResponse.TextMaxLength"/> and the modules check where each text is written; the shared
/// validators check only <see cref="Ceiling"/>.
/// </summary>
public static class MemberTextLimits
{
    /// <summary>
    /// The technical ceiling on a member's text, whatever the setting says: a guard against a runaway request, not a rule.
    /// The same number as <c>SettingKeys.MemberTextCeiling</c> (a unit test).
    /// </summary>
    public const int Ceiling = 20_000;

    /// <summary>The longest text the setting allows (0 = no limit, then the ceiling): what a form lets one type.</summary>
    public static int Allowed(int setting) => setting <= 0 ? Ceiling : Math.Min(setting, Ceiling);

    /// <summary>A whole number as the interface writes it, "20 000" (a no-break space), for the shared validators' messages.</summary>
    internal static string Number(int value) =>
        value.ToString("#,0", new System.Globalization.NumberFormatInfo { NumberGroupSeparator = " ", NumberGroupSizes = [3] });
}
