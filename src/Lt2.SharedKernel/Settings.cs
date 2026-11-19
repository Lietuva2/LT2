using System.Text.Json;

namespace Lt2.SharedKernel;

/// <summary>A runtime setting (design 5.6): stored in <c>config.setting</c>, changed by an administrator without a deploy.</summary>
public interface ISettingKey
{
    string Name { get; }

    JsonElement DefaultValue { get; }

    /// <summary>Whether <paramref name="value"/> has the right type and range for this setting.</summary>
    bool IsValid(JsonElement value);
}

public sealed class SettingKey<T> : ISettingKey
{
    private readonly Func<T, bool> _isValid;

    public SettingKey(string name, T defaultValue, Func<T, bool>? isValid = null)
    {
        Name = name;
        Default = defaultValue;
        _isValid = isValid ?? (_ => true);
    }

    public string Name { get; }

    public T Default { get; }

    public JsonElement DefaultValue => JsonSerializer.SerializeToElement(Default, JsonSerializerOptions.Web);

    public bool IsValid(JsonElement value)
    {
        if (!TryRead(value, out var typed))
        {
            return false;
        }

        return _isValid(typed);
    }

    public bool TryRead(JsonElement value, out T result)
    {
        result = Default;
        try
        {
            var read = value.Deserialize<T>(JsonSerializerOptions.Web);
            if (read is null)
            {
                return false;
            }

            result = read;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>Every runtime setting the application knows, with the defaults from the design.</summary>
public static class SettingKeys
{
    /// <summary>Whether registration needs an invitation code (closed trial, design 6.2).</summary>
    public static readonly SettingKey<bool> RegistrationRequiresInvitation = new("Registration:RequireInvitation", true);

    /// <summary>
    /// Design 8.5.1 <b>[the designer's proposal, awaiting the owner]</b>: while registration needs an invitation, an MP's
    /// official address may register without one (the mailbox is the proof). Off by default: on a real server it is switched
    /// on only together with the introduction letter (8.4); the local stack and the trial, whose MPs are invented and whose
    /// email is caught, switch it on (the migrator's <c>--official-addresses-without-invitation</c>).
    /// </summary>
    public static readonly SettingKey<bool> RegistrationOfficialAddressesWithoutInvitation = new("Registration:OfficialAddressesWithoutInvitation", false);

    /// <summary>
    /// Registrations one personal invitation code may bring (design 6.2); 0 = no limit, the default (decided by the owner
    /// on 2026-11-13). Team codes keep their own uses.
    /// </summary>
    public static readonly SettingKey<int> RegistrationPersonalCodeUses = new("Registration:PersonalCodeUses", 0, v => v is >= 0 and <= 100000);

    /// <summary>
    /// Registrations one organisation's invitation code may bring (design 8.14.14, stage B); 0 = no limit, the default.
    /// The personal codes' twin.
    /// </summary>
    public static readonly SettingKey<int> RegistrationOrganisationCodeUses = new("Registration:OrganisationCodeUses", 0, v => v is >= 0 and <= 100000);

    /// <summary>Months feedback from members is kept before the daily clean-up deletes it (design 8.10).</summary>
    public static readonly SettingKey<int> FeedbackKeepMonths = new("Feedback:KeepMonths", 12, v => v is > 0 and <= 60);

    /// <summary>Days a pending account (address never proven, or registration never completed) is kept (design 6.2).</summary>
    public static readonly SettingKey<int> PendingAccountDays = new("Registration:PendingAccountDays", 7, v => v is > 0 and <= 90);

    /// <summary>Sign-in codes one address may be sent in an hour (design 6.2).</summary>
    public static readonly SettingKey<int> SignInCodesPerHour = new("SignIn:CodesPerHour", 5, v => v is > 0 and <= 20);

    /// <summary>Sign-in codes one address may be sent in a day; bounds guessing to five tries per code (design 6.2).</summary>
    public static readonly SettingKey<int> SignInCodesPerDay = new("SignIn:CodesPerDay", 10, v => v is > 0 and <= 50);

    /// <summary>
    /// Whether registration and a sign-in code request must carry a solved proof of work (design 6.2, 6.4); on in every
    /// deployment. Off only to stop a check that fails for real people while it is looked into.
    /// </summary>
    public static readonly SettingKey<bool> BotCheckEnabled = new("BotCheck:Enabled", true);

    /// <summary>
    /// The difficulty of the proof of work: the largest secret number a challenge can hide (design 6.4). The browser tries
    /// numbers from 0 up, so half of it is the average work; SHA-256 per try.
    /// </summary>
    public static readonly SettingKey<int> BotCheckMaxNumber = new("BotCheck:MaxNumber", 20_000, v => v is >= 1_000 and <= 1_000_000);

    /// <summary>Minutes a challenge may be solved and used in (design 6.4); a page left open longer asks for a new one itself.</summary>
    public static readonly SettingKey<int> BotCheckChallengeMinutes = new("BotCheck:ChallengeMinutes", 10, v => v is >= 1 and <= 60);

    /// <summary>The threshold of every bill without its own (design 8.12.2), read whenever it is used.</summary>
    public static readonly SettingKey<int> DigestThresholdDefault = new("Digest:ThresholdDefault", 100, v => v > 0);

    /// <summary>
    /// The administrator's minimum threshold (design 8.12.2): no moderator may set less, and a default below it is lifted to it. The design
    /// names the setting but gives no value; 50 is a placeholder for the owner to set.
    /// </summary>
    public static readonly SettingKey<int> DigestThresholdMinimum = new("Digest:ThresholdMinimum", 50, v => v > 0);

    /// <summary>The weekly summary: Monday at this Vilnius time ("HH:mm"), or "off" (design 8.4).</summary>
    public static readonly SettingKey<string> DigestWeekly = new("Digest:Weekly", "07:00", IsTimeOrOff);

    /// <summary>The before-vote summary: on the working day before the sitting at this Vilnius time, or "off" (design 8.4).</summary>
    public static readonly SettingKey<string> DigestBeforeVote = new("Digest:BeforeVote", "12:00", IsTimeOrOff);

    /// <summary>Most summaries an MP receives in a week (design 8.4).</summary>
    public static readonly SettingKey<int> DigestMaxPerWeek = new("Digest:MaxPerWeek", 2, v => v is > 0 and <= 7);

    /// <summary>
    /// "Changed notably" for the weekly summary (design 8.4): the share by which a record's participants (both sides)
    /// changed since the last summary that held it. Decided by the owner on 2026-11-13, default 10 %.
    /// </summary>
    public static readonly SettingKey<decimal> DigestNotableChange = new("Digest:NotableChange", 0.10m, v => v is > 0 and <= 1);

    /// <summary>
    /// Design 8.19.1: whether summaries really go to MPs. <c>off</c> (the default: prepared and reviewed the same way, then made
    /// as a dry run, nothing sent) or <c>automatic</c> (sent at its time). Whatever it says, nothing goes before the
    /// introduction letter, nor where the data is invented and the mail is not caught.
    /// </summary>
    public static readonly SettingKey<string> DigestSending = new("Digest:Sending", "off", v => v is "off" or "automatic");

    /// <summary>
    /// Design 8.19.2 (owner, 2026-11-17): a weekly or before-vote summary is prepared so many hours before its sending time
    /// (Vilnius wall-clock hours), so moderators can review it: which bills and which arguments are fixed then; the numbers
    /// are read when it goes.
    /// </summary>
    public static readonly SettingKey<int> DigestReviewHours = new("Digest:ReviewHours", 24, v => v is >= 1 and <= 72);

    /// <summary>
    /// Design 6.3, 8.4 (owner, 2026-11-17): the smallest group of participants on a side who said they support the MP's party
    /// for that side's party number to be shown in a summary; 0 = no floor (any group of one or more). Default 10, the
    /// designer's recommendation the owner did not object to; the lawyer's reading before launch may set another value.
    /// </summary>
    public static readonly SettingKey<int> DigestPartyGroupMinimum = new("Digest:PartyGroupMinimum", 10, v => v is >= 0 and <= 1000);

    /// <summary>
    /// Arguments one person may write on one bill (design 8.2); 0 = no limit, the default (decided by the owner on
    /// 2026-11-13: limits are switched on when the need shows).
    /// </summary>
    public static readonly SettingKey<int> AppealsReasonsPerPerson = new("Appeals:ReasonsPerPerson", 0, v => v is >= 0 and <= 100);

    /// <summary>
    /// An argument with fewer words than this is too short (owner, 2026-11-17): the form asks first, and one published anyway
    /// is flagged <c>too-short</c> and waits for a moderator. 0 = off. Arguments only.
    /// </summary>
    public static readonly SettingKey<int> AppealsMinReasonWords = new("Appeals:MinReasonWords", 3, v => v is >= 0 and <= 50);

    /// <summary>
    /// The longest text, in characters, of what members write for others to read (design 5.6, 8.2, owner 2026-11-16): an
    /// argument, an organisation's statement, an MP's response (and comments and "Apie mane" when they are built). Texts a
    /// person reviews anyway (a bill's description by a moderator) and short fields keep their own limits. 0 = no limit but
    /// <see cref="MemberTextCeiling"/>. A lower number later shortens and removes nothing already written.
    /// </summary>
    public static readonly SettingKey<int> TextsMaxLength = new("Texts:MaxLength", 2000, v => v is >= 0 and <= MemberTextCeiling);

    /// <summary>
    /// The one technical ceiling on a member's text (<see cref="TextsMaxLength"/>), whatever the setting says: a guard
    /// against a runaway request or script, not a rule of the product. The HTTP validators check only this; the setting is
    /// checked where each text is written (design 6.8). <c>MemberTextLimits.Ceiling</c> in the contracts is the same
    /// number (a unit test).
    /// </summary>
    public const int MemberTextCeiling = MemberTextLength.Ceiling;

    /// <summary>"Man svarbu" marks one person may give on one bill (design 8.2); 0 = no limit, the default.</summary>
    public static readonly SettingKey<int> AppealsEndorsementsPerPerson = new("Appeals:EndorsementsPerPerson", 0, v => v is >= 0 and <= 100);


    /// <summary>
    /// The Claude model that drafts record descriptions (design 8.11.4; provider decided by the owner on 2026-11-13). Used
    /// only when the provider is configured.
    /// </summary>
    public static readonly SettingKey<string> AiDraftModel = new("Ai:DraftModel", "claude-sonnet-5-5", v => !string.IsNullOrWhiteSpace(v) && v.Length <= 100);

    /// <summary>
    /// AI summaries the summary job may ask the provider for in one Vilnius day (design 8.13); 0 switches the job off.
    /// Used only when a provider is configured and a bill's official text can be read.
    /// </summary>
    public static readonly SettingKey<int> AiSummariesPerDay = new("Ai:SummariesPerDay", 30, v => v is >= 0 and <= 1000);

    /// <summary>
    /// Organisations one account may register (design 8.14.14, stage A): keeps one person from flooding the list. Counts
    /// the organisations the account registered that are not closed (a blocked one counts).
    /// </summary>
    public static readonly SettingKey<int> OrganisationsPerAccount = new("Organisations:PerAccount", 3, v => v is > 0 and <= 100);

    /// <summary>
    /// Design 8.20.1: the Vilnius time ("HH:mm") on the morning after MPs answered on a followed bill when the day's email
    /// "Seimo narių atsakymai" goes to the followers.
    /// </summary>
    public static readonly SettingKey<string> FollowsResponsesAt = new("Follows:ResponsesAt", "08:00", IsTime);


    /// <summary>Design 8.23.5 (owner, 2026-11-16): comments under arguments take new ones; off keeps the written ones readable.</summary>
    public static readonly SettingKey<bool> CommentsOnReasons = new("Comments:OnReasons", true);

    /// <summary>Design 8.23.5: comments under organisations' statements take new ones.</summary>
    public static readonly SettingKey<bool> CommentsOnStatements = new("Comments:OnStatements", true);

    /// <summary>Design 8.23.5: comments under MPs' responses take new ones (on during the trial; decided again before real MPs, 12).</summary>
    public static readonly SettingKey<bool> CommentsOnResponses = new("Comments:OnResponses", true);

    /// <summary>
    /// Design 8.23.4: shown comments one person may have under one text; 0 = no limit, the default. Switched on later it removes
    /// nothing.
    /// </summary>
    public static readonly SettingKey<int> CommentsPerPersonPerTarget = new("Comments:PerPersonPerTarget", 0, v => v is >= 0 and <= 1000);

    /// <summary>Surge note: share of recent positions from new accounts (design 6.6).</summary>
    public static readonly SettingKey<decimal> SurgeMinimumShare = new("Surge:MinimumShare", 0.30m, v => v is > 0 and <= 1);

    /// <summary>Surge note: minimum number of new accounts (design 6.6).</summary>
    public static readonly SettingKey<int> SurgeMinimumAccounts = new("Surge:MinimumAccounts", 50, v => v > 0);

    /// <summary>Surge note: window in hours (design 6.6).</summary>
    public static readonly SettingKey<int> SurgeWindowHours = new("Surge:WindowHours", 72, v => v is > 0 and <= 24 * 30);

    public static readonly IReadOnlyList<ISettingKey> All =
    [
        RegistrationRequiresInvitation,
        RegistrationOfficialAddressesWithoutInvitation,
        RegistrationPersonalCodeUses,
        RegistrationOrganisationCodeUses,
        FeedbackKeepMonths,
        PendingAccountDays,
        SignInCodesPerHour,
        SignInCodesPerDay,
        BotCheckEnabled,
        BotCheckMaxNumber,
        BotCheckChallengeMinutes,
        DigestThresholdDefault,
        DigestThresholdMinimum,
        DigestWeekly,
        DigestBeforeVote,
        DigestMaxPerWeek,
        DigestNotableChange,
        DigestSending,
        DigestReviewHours,
        DigestPartyGroupMinimum,
        AppealsReasonsPerPerson,
        AppealsMinReasonWords,
        TextsMaxLength,
        AppealsEndorsementsPerPerson,
        OrganisationsPerAccount,
        FollowsResponsesAt,
        CommentsOnReasons,
        CommentsOnStatements,
        CommentsOnResponses,
        CommentsPerPersonPerTarget,
        AiDraftModel,
        AiSummariesPerDay,
        SurgeMinimumShare,
        SurgeMinimumAccounts,
        SurgeWindowHours,
    ];

    private static bool IsTimeOrOff(string value) => value == "off" || IsTime(value);

    private static bool IsTime(string value) =>
        TimeOnly.TryParseExact(value, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);

    public static ISettingKey? Find(string name) =>
        All.FirstOrDefault(k => string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Reads runtime settings. Implemented by the Audit module, which owns <c>config.setting</c>.</summary>
public interface ISettings
{
    Task<T> GetAsync<T>(SettingKey<T> key, CancellationToken cancellationToken = default);
}
