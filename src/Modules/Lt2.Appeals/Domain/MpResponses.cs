namespace Lt2.Appeals.Domain;

/// <summary>
/// <c>appeals.mp_response</c> (design 5.3, 8.5.5): an MP's public answer on a bill, one per MP per bill, keyed by the Seimas's
/// person number and the bill's number. The module knows nothing of the MP's link beyond the account that last wrote it
/// (<see cref="WrittenByUserId"/>), which is never in a public answer and is emptied when that account goes. A response
/// written again after a withdrawal is the same row: its history continues.
/// </summary>
internal sealed class MpResponse
{
    public Guid Id { get; set; }

    public string BillNumber { get; set; } = "";

    public long SeimasPersonId { get; set; }

    /// <summary>See <see cref="ResponseStances"/>.</summary>
    public string Stance { get; set; } = "";

    public string Text { get; set; } = "";

    /// <summary>See <see cref="ResponseStatuses"/>.</summary>
    public string Status { get; set; } = ResponseStatuses.Published;

    /// <summary>What pre-screening found in the text now shown: the response is public at once and in the queue (design 8.5.5).</summary>
    public string[] ScreeningFlags { get; set; } = [];

    /// <summary>When a moderator last looked; a flagged response is in the queue while null.</summary>
    public DateTimeOffset? SeenAt { get; set; }

    /// <summary>When it was first published; never moves.</summary>
    public DateTimeOffset PublishedAt { get; set; }

    /// <summary>
    /// When the stance now shown became public (design 8.20.1, the followers' email): the first publication, or a later one
    /// that changed the stance (<see cref="ResponseRules.Announces"/>). A changed explanation with the same stance, a withdrawal
    /// and a moderator's restoring do not move it.
    /// </summary>
    public DateTimeOffset AnnouncedAt { get; set; }

    /// <summary>When the text now shown was written, if it is not the first ("Keista").</summary>
    public DateTimeOffset? EditedAt { get; set; }

    /// <summary>While withdrawn: when ("Atsakymas atsiimtas"). Cleared when it is written again.</summary>
    public DateTimeOffset? WithdrawnAt { get; set; }

    /// <summary>While hidden by a moderator: when.</summary>
    public DateTimeOffset? HiddenAt { get; set; }

    /// <summary>While hidden: the moderator's statement, shown in its place (design 8.5.5).</summary>
    public string? HideStatement { get; set; }

    /// <summary>The account (the MP's or an office member's) that last wrote or withdrew it. Never public; emptied with the account.</summary>
    public Guid? WrittenByUserId { get; set; }
}

/// <summary>An MP's stance (design 8.5.5): "balsuosiu už", "balsuosiu prieš", "dar neapsisprendžiau".</summary>
internal static class ResponseStances
{
    public const string For = "for";
    public const string Against = "against";
    public const string Undecided = "undecided";

    public static readonly IReadOnlyList<string> All = [For, Against, Undecided];
}

internal static class ResponseStatuses
{
    public const string Published = "published";

    /// <summary>Withdrawn by the MP's side: still shown, as "Atsakymas atsiimtas", with its versions (design 8.5.5).</summary>
    public const string Withdrawn = "withdrawn";

    /// <summary>Hidden by a moderator: its place shows the moderator's statement; "Atkurti" while the bill is open.</summary>
    public const string Hidden = "hidden";

    public static readonly IReadOnlyList<string> All = [Published, Withdrawn, Hidden];
}

/// <summary>
/// The rules of an MP's response (design 8.5.5), as pure functions: what may be written, when it is frozen, what a change
/// keeps in the public history, and the order the bill's page lists responses in. Never a sum, share or score of answers
/// against ballots: there is no such function on purpose (design 7.1).
/// </summary>
internal static class ResponseRules
{
    /// <summary>
    /// The response's stance and text: null when they may be published, otherwise the error's code. The text is not blank
    /// and no longer than the setting <c>Texts:MaxLength</c> allows (<paramref name="maxLength"/>; owner, 2026-11-16).
    /// </summary>
    public static string? CheckResponse(string? stance, string? text, int maxLength)
    {
        if (stance is null || !ResponseStances.All.Contains(stance))
        {
            return "response.stance-invalid";
        }

        var normalised = AppealRules.NormaliseText(text);
        return normalised.Length == 0 ? "response.empty" : AppealRules.TooLong(normalised, maxLength) ? "response.too-long" : null;
    }

    /// <summary>
    /// Written, changed and withdrawn only until the adoption vote, as a side (design 8.5.5): the bill is active and its record,
    /// if any, open. A hidden record (taking part closed by the moderators) takes none either.
    /// </summary>
    public static string? CheckBillOpen(string? recordStatus, bool billFinished) =>
        RecordRules.CheckParticipation(recordStatus, billFinished) is null ? null : "response.bill-not-open";

    /// <summary>A response a moderator hid is the moderator's to restore; the MP's side cannot write over it.</summary>
    public static string? CheckWritable(string? currentStatus) => currentStatus == ResponseStatuses.Hidden ? "response.hidden" : null;

    /// <summary>
    /// Whether replacing the current text keeps it as an earlier version: a published text always does (it was public). A
    /// withdrawn one was kept when it was withdrawn, so writing again adds nothing twice (design 8.5.5: nothing is silently
    /// replaced).
    /// </summary>
    public static bool KeepsVersion(string currentStatus) => currentStatus == ResponseStatuses.Published;

    /// <summary>The same stance and text as now shown: saving it again changes nothing and adds no version.</summary>
    public static bool Unchanged(string currentStatus, string currentStance, string currentText, string stance, string text) =>
        currentStatus == ResponseStatuses.Published && currentStance == stance && currentText == text;

    /// <summary>
    /// Whether saving a response is an event for the followers (design 8.20.1): a first publication, or one that changes the
    /// stance from the one the response had (shown now, or when it was withdrawn). A changed explanation with the same stance is
    /// not, and neither is writing a withdrawn response again with the stance it had.
    /// </summary>
    /// <param name="currentStance">The stance the response has now; null when there is no response yet.</param>
    public static bool Announces(string? currentStance, string stance) => currentStance != stance;

    /// <summary>Only a published response can be withdrawn.</summary>
    public static string? CheckWithdraw(string? currentStatus) => currentStatus == ResponseStatuses.Published ? null : "response.not-found";

    /// <summary>A moderator hides what is shown: a published response or a withdrawn one with its versions.</summary>
    public static bool MayHide(string status) => status is ResponseStatuses.Published or ResponseStatuses.Withdrawn;

    /// <summary>"Palikti": a moderator looked at a shown response and leaves it as it is.</summary>
    public static bool MayKeep(string status) => MayHide(status);

    /// <summary>A hidden response comes back only while the bill is open (design 8.5.5).</summary>
    public static bool MayRestore(string status, bool billOpen) => status == ResponseStatuses.Hidden && billOpen;

    /// <summary>What a restored response becomes: withdrawn again if it was withdrawn when hidden, otherwise published.</summary>
    public static string RestoredStatus(DateTimeOffset? withdrawnAt) => withdrawnAt is null ? ResponseStatuses.Published : ResponseStatuses.Withdrawn;

    /// <summary>The bill's page lists "už", "prieš", "neapsisprendė", then withdrawn and hidden ones (design 8.5.6).</summary>
    public static int ListOrder(string status, string stance) => status != ResponseStatuses.Published ? 3 : stance switch
    {
        ResponseStances.For => 0,
        ResponseStances.Against => 1,
        _ => 2,
    };
}
