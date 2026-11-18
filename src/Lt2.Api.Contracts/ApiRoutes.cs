namespace Lt2.Api.Contracts;

/// <summary>Every API route, shared by the endpoints and the HTTP clients.</summary>
public static class ApiRoutes
{
    public const string Prefix = "/api/v1";

    public static class Auth
    {
        public const string Group = Prefix + "/auth";
        public const string Register = Group + "/register";
        public const string Code = Group + "/code";
        public const string SignIn = Group + "/sign-in";
        public const string SignOut = Group + "/sign-out";
        public const string Antiforgery = Group + "/antiforgery";
        public const string PasskeyCreationOptions = Group + "/passkeys/creation-options";
        public const string Passkeys = Group + "/passkeys";
        public const string PasskeyRequestOptions = Group + "/passkeys/request-options";
        public const string PasskeySignIn = Group + "/passkeys/sign-in";
    }

    public static class Me
    {
        public const string Root = Prefix + "/me";
        public const string Registration = Root + "/registration";
        public const string RoleActivation = Root + "/role-activation";
        public const string PasskeyConfirmation = Root + "/passkey-confirmation";

        /// <summary>A code session on a staff account becomes a staff session with the staff passkey (design 6.2).</summary>
        public const string StaffSession = Root + "/staff-session";

        /// <summary>The session becomes a passkey session with one of the account's own passkeys, in place (owner, 2026-11-17).</summary>
        public const string PasskeySession = Root + "/passkey-session";

        /// <summary>Changing the display name (design 6.2).</summary>
        public const string DisplayName = Root + "/display-name";
        public const string Sessions = Root + "/sessions";
        public const string Session = Root + "/sessions/{id:guid}";

        /// <summary>The browsers known to the account (design 8.20.2) and forgetting one.</summary>
        public const string KnownBrowsers = Root + "/known-browsers";
        public const string KnownBrowser = Root + "/known-browsers/{id:guid}";
        public const string PositionConsent = Root + "/consents/positions";

        /// <summary>Accepting the terms of use in force (design 8.22.2).</summary>
        public const string Terms = Root + "/consents/terms";
        public const string PartySupport = Root + "/party-support";

        /// <summary>
        /// <c>PUT</c>: the party shown on the public profile, with the consent <c>party-public</c>; <c>DELETE</c>: no longer shown
        /// (design 8.25.9; open behind the terms, 8.22.2).
        /// </summary>
        public const string PartyPublic = Root + "/party-support/public";
        public const string Export = Root + "/export";
        public const string Invitation = Root + "/invitation";
        public const string ReplaceInvitation = Root + "/invitation/replace";

        /// <summary>The caller's own side and follow for the bills asked about (design 8.7).</summary>
        public const string Bills = Root + "/bills";

        /// <summary>The caller opened a bill's page (design 8.11.8): the moment kept on their follow, and what is new since the one before.</summary>
        public const string BillSeen = Root + "/bills/{number}/seen";

        public static string BillSeenOf(string number) => $"{Root}/bills/{Uri.EscapeDataString(number)}/seen";

        public static string SessionById(Guid id) => $"{Sessions}/{id}";

        public static string KnownBrowserById(Guid id) => $"{KnownBrowsers}/{id}";
    }

    public static class Admin
    {
        public const string Settings = Prefix + "/admin/settings";
        public const string Setting = Prefix + "/admin/settings/{key}";
        public const string Roles = Prefix + "/admin/roles";
        public const string ResetAccount = Prefix + "/admin/accounts/reset";

        public static string SettingByKey(string key) => $"{Settings}/{Uri.EscapeDataString(key)}";

        public const string Invitations = Prefix + "/admin/invitations";
        public const string SwitchOffInvitation = Prefix + "/admin/invitations/{id:guid}/switch-off";
        public const string SwitchOnInvitation = Prefix + "/admin/invitations/{id:guid}/switch-on";

        public static string SwitchInvitation(Guid id, bool on) => $"{Invitations}/{id}/{(on ? "switch-on" : "switch-off")}";

        /// <summary>Design 8.27: the site's own usage statistics (<c>?days=</c> for the pages and referring sites).</summary>
        public const string Usage = Prefix + "/admin/usage";
    }
}

/// <summary>Design 7.4: the public parliament routes.</summary>
public static class ParliamentRoutes
{
    public const string CurrentTerm = ApiRoutes.Prefix + "/terms/current";
    public const string Mps = ApiRoutes.Prefix + "/mps";
    public const string Mp = Mps + "/{slug}";
    public const string MpVotes = Mps + "/{slug}/votes";
    public const string Factions = ApiRoutes.Prefix + "/factions";
    public const string Faction = Factions + "/{slug}";
    public const string Committees = ApiRoutes.Prefix + "/committees";
    public const string Committee = Committees + "/{slug}";
    public const string Bills = ApiRoutes.Prefix + "/bills";
    public const string Bill = Bills + "/{number}";
    public const string Votes = ApiRoutes.Prefix + "/votes";
    public const string Vote = Votes + "/{id:long}";
    public const string Imports = ApiRoutes.Prefix + "/meta/imports";

    public static string MpBySlug(string slug) => $"{Mps}/{Uri.EscapeDataString(slug)}";

    public static string MpVotesBySlug(string slug) => $"{MpBySlug(slug)}/votes";

    public static string FactionBySlug(string slug) => $"{Factions}/{Uri.EscapeDataString(slug)}";

    public static string CommitteeBySlug(string slug) => $"{Committees}/{Uri.EscapeDataString(slug)}";

    public static string BillByNumber(string number) => $"{Bills}/{Uri.EscapeDataString(number)}";

    public static string VoteById(long id) => $"{ApiRoutes.Prefix}/votes/{id.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>Design 8.8, 8.12: bills as people take part on them, positions, arguments, endorsements, reports and moderation.</summary>
public static class AppealRoutes
{
    public const string Appeals = ApiRoutes.Prefix + "/appeals";
    public const string Rules = Appeals + "/rules";
    public const string Record = ApiRoutes.Prefix + "/bills/{number}/record";
    public const string Mine = Record + "/mine";
    public const string History = Record + "/history";

    public static string HistoryOf(string number) => RecordOf(number) + "/history";

    /// <summary>Design 8.12.3: a moderator's state of a bill, its record for an action, and its description; the AI draft (8.11.4).</summary>
    public const string ModerationBill = ApiRoutes.Prefix + "/moderation/bills/{number}";
    public const string BillRecord = ModerationBill + "/record";
    public const string BillDescription = ModerationBill + "/description";

    /// <summary>Design 8.13: a moderator confirms an AI summary nobody has read, or removes a description.</summary>
    public const string ConfirmDescription = BillDescription + "/confirm";
    public const string RemoveDescription = BillDescription + "/remove";
    public const string Ai = ApiRoutes.Prefix + "/moderation/ai";
    public const string DescriptionDraft = ModerationBill + "/description-draft";

    public static string ModerationBillOf(string number) => $"{ApiRoutes.Prefix}/moderation/bills/{Uri.EscapeDataString(number)}";

    public static string BillRecordOf(string number) => ModerationBillOf(number) + "/record";

    public static string BillDescriptionOf(string number) => ModerationBillOf(number) + "/description";

    public static string ConfirmDescriptionOf(string number) => BillDescriptionOf(number) + "/confirm";

    public static string RemoveDescriptionOf(string number) => BillDescriptionOf(number) + "/remove";

    public static string DescriptionDraftOf(string number) => ModerationBillOf(number) + "/description-draft";

    public const string Position = Record + "/position";
    public const string Reasons = Record + "/reasons";
    public const string SimilarReasons = Record + "/reasons/similar";
    public const string Endorsement = ApiRoutes.Prefix + "/reasons/{id:guid}/endorsement";

    /// <summary><c>PUT</c>: the author edits it until someone reacts (design 8.2); <c>DELETE</c>: the author withdraws it.</summary>
    public const string Reason = ApiRoutes.Prefix + "/reasons/{id:guid}";
    public const string Reports = ApiRoutes.Prefix + "/reports";
    public const string Flags = Record + "/flags";
    public const string RecordPlanned = ApiRoutes.Prefix + "/moderation/records/{id:guid}/planned";
    public const string HoldRecord = ApiRoutes.Prefix + "/moderation/records/{id:guid}/hold";
    public const string ReleaseRecord = ApiRoutes.Prefix + "/moderation/records/{id:guid}/release";

    public static string FlagsOf(string number) => RecordOf(number) + "/flags";

    public const string ModerationQueue = ApiRoutes.Prefix + "/moderation/queue";
    public const string MyRestriction = ApiRoutes.Prefix + "/me/writing-restriction";
    public const string ReasonAuthor = ApiRoutes.Prefix + "/moderation/reasons/{id:guid}/author";

    public const string RestrictReasonAuthor = ApiRoutes.Prefix + "/moderation/reasons/{id:guid}/restrict-author";

    public const string Restrictions = ApiRoutes.Prefix + "/moderation/restrictions";
    public const string LiftRestriction = ApiRoutes.Prefix + "/moderation/restrictions/{id:guid}/lift";
    public const string KeepRecord = ApiRoutes.Prefix + "/moderation/records/{id:guid}/keep";
    public const string HideRecord = ApiRoutes.Prefix + "/moderation/records/{id:guid}/hide";
    public const string Hidden = ApiRoutes.Prefix + "/moderation/hidden";
    public const string RestoreRecord = ApiRoutes.Prefix + "/moderation/records/{id:guid}/restore";
    public const string RestoreReason = ApiRoutes.Prefix + "/moderation/reasons/{id:guid}/restore";
    public const string RecordThreshold = ApiRoutes.Prefix + "/moderation/records/{id:guid}/threshold";

    public const string PublishReason = ApiRoutes.Prefix + "/moderation/reasons/{id:guid}/publish";
    public const string HideReason = ApiRoutes.Prefix + "/moderation/reasons/{id:guid}/hide";
    public const string MergeReason = ApiRoutes.Prefix + "/moderation/reasons/{id:guid}/merge";

    public static string RecordOf(string number) => $"{ApiRoutes.Prefix}/bills/{Uri.EscapeDataString(number)}/record";

    public static string MineOf(string number) => RecordOf(number) + "/mine";


    public static string PositionOf(string number) => RecordOf(number) + "/position";

    public static string ReasonsOf(string number) => RecordOf(number) + "/reasons";

    /// <summary>
    /// <c>GET</c> one page of a side's arguments (design 8.12.1, owner 2026-11-19): <paramref name="limit"/> of them
    /// (<see cref="Appeals.ReasonLimits"/>) from <paramref name="cursor"/> on.
    /// </summary>
    public static string ReasonPageOf(string number, string side, string? cursor = null, int? limit = null) =>
        ReasonsOf(number) + "?side=" + Uri.EscapeDataString(side)
        + (string.IsNullOrEmpty(cursor) ? "" : "&cursor=" + Uri.EscapeDataString(cursor))
        + (limit is { } n ? "&limit=" + n.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");

    public static string SimilarReasonsOf(string number) => RecordOf(number) + "/reasons/similar";

    public static string EndorsementOf(Guid reasonId) => $"{ApiRoutes.Prefix}/reasons/{reasonId}/endorsement";

    public static string ReasonOf(Guid reasonId) => $"{ApiRoutes.Prefix}/reasons/{reasonId}";

    /// <summary>
    /// <c>GET /appeals</c> (design 8.12.5): <c>q</c> searches number and title, <c>planned=true</c> keeps those planned soon,
    /// <c>tab=closed</c> lists the closed ones, <c>sort</c> is <c>activity</c> (the default), <c>participants</c>,
    /// <c>arguments</c>, <c>planned</c> or <c>latest</c> (the last activity first; owner, 2026-11-17); <c>limit</c> how many
    /// (<see cref="Appeals.BillListLimits"/>), <c>cursor</c> where to start.
    /// </summary>
    public static string AppealsList(string? search, bool planned, bool closed, string? cursor, string? sort = null, int? limit = null)
    {
        var query = new List<string>();
        if (!string.IsNullOrEmpty(sort))
        {
            query.Add("sort=" + Uri.EscapeDataString(sort));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query.Add("q=" + Uri.EscapeDataString(search.Trim()));
        }

        if (planned)
        {
            query.Add("planned=true");
        }

        if (closed)
        {
            query.Add("tab=closed");
        }

        if (!string.IsNullOrEmpty(cursor))
        {
            query.Add("cursor=" + Uri.EscapeDataString(cursor));
        }

        if (limit is { } n)
        {
            query.Add("limit=" + n.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return query.Count == 0 ? Appeals : Appeals + "?" + string.Join("&", query);
    }

    /// <summary>A moderation route with the target id filled in.</summary>
    public static string With(string route, Guid id) => route.Replace("{id:guid}", id.ToString(), StringComparison.Ordinal);
}

/// <summary>Header names the API and its clients agree on.</summary>
public static class ApiHeaders
{
    /// <summary>Antiforgery request token header, required on every state-changing call.</summary>
    public const string Antiforgery = "X-XSRF-TOKEN";
}
