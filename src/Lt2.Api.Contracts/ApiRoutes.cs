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
        public const string Sessions = Root + "/sessions";
        public const string Session = Root + "/sessions/{id:guid}";
        public const string PositionConsent = Root + "/consents/positions";
        public const string PartySupport = Root + "/party-support";

        public static string SessionById(Guid id) => $"{Sessions}/{id}";
    }

    public static class Admin
    {
        public const string Settings = Prefix + "/admin/settings";
        public const string Setting = Prefix + "/admin/settings/{key}";
        public const string Roles = Prefix + "/admin/roles";
        public const string ResetAccount = Prefix + "/admin/accounts/reset";

        public static string SettingByKey(string key) => $"{Settings}/{Uri.EscapeDataString(key)}";
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
    public const string Vote = ApiRoutes.Prefix + "/votes/{id:long}";
    public const string Imports = ApiRoutes.Prefix + "/meta/imports";

    public static string MpBySlug(string slug) => $"{Mps}/{Uri.EscapeDataString(slug)}";

    public static string MpVotesBySlug(string slug) => $"{MpBySlug(slug)}/votes";

    public static string FactionBySlug(string slug) => $"{Factions}/{Uri.EscapeDataString(slug)}";

    public static string CommitteeBySlug(string slug) => $"{Committees}/{Uri.EscapeDataString(slug)}";

    public static string BillByNumber(string number) => $"{Bills}/{Uri.EscapeDataString(number)}";

    public static string VoteById(long id) => $"{ApiRoutes.Prefix}/votes/{id.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>Design 8.8: appeals, positions, arguments, endorsements, reports and moderation.</summary>
public static class AppealRoutes
{
    public const string Appeals = ApiRoutes.Prefix + "/appeals";
    public const string Rules = Appeals + "/rules";
    public const string Appeal = ApiRoutes.Prefix + "/bills/{number}/appeal";
    public const string Mine = Appeal + "/mine";
    public const string Threshold = Appeal + "/threshold";
    public const string Position = Appeal + "/position";
    public const string Reasons = Appeal + "/reasons";
    public const string SimilarReasons = Appeal + "/reasons/similar";
    public const string Endorsement = ApiRoutes.Prefix + "/reasons/{id:guid}/endorsement";
    public const string Reports = ApiRoutes.Prefix + "/reports";
    public const string Flags = Appeal + "/flags";
    public const string AppealPlanned = ApiRoutes.Prefix + "/moderation/appeals/{id:guid}/planned";
    public const string HoldAppeal = ApiRoutes.Prefix + "/moderation/appeals/{id:guid}/hold";
    public const string ReleaseAppeal = ApiRoutes.Prefix + "/moderation/appeals/{id:guid}/release";

    public static string FlagsOf(string number) => AppealOf(number) + "/flags";

    public const string ModerationQueue = ApiRoutes.Prefix + "/moderation/queue";
    public const string PublishAppeal = ApiRoutes.Prefix + "/moderation/appeals/{id:guid}/publish";
    public const string ReturnAppeal = ApiRoutes.Prefix + "/moderation/appeals/{id:guid}/return";
    public const string HideAppeal = ApiRoutes.Prefix + "/moderation/appeals/{id:guid}/hide";
    public const string AppealThreshold = ApiRoutes.Prefix + "/moderation/appeals/{id:guid}/threshold";
    public const string PublishReason = ApiRoutes.Prefix + "/moderation/reasons/{id:guid}/publish";
    public const string HideReason = ApiRoutes.Prefix + "/moderation/reasons/{id:guid}/hide";
    public const string MergeReason = ApiRoutes.Prefix + "/moderation/reasons/{id:guid}/merge";

    public static string AppealOf(string number) => $"{ApiRoutes.Prefix}/bills/{Uri.EscapeDataString(number)}/appeal";

    public static string MineOf(string number) => AppealOf(number) + "/mine";

    public static string ThresholdOf(string number) => AppealOf(number) + "/threshold";

    public static string PositionOf(string number) => AppealOf(number) + "/position";

    public static string ReasonsOf(string number) => AppealOf(number) + "/reasons";

    public static string SimilarReasonsOf(string number) => AppealOf(number) + "/reasons/similar";

    public static string EndorsementOf(Guid reasonId) => $"{ApiRoutes.Prefix}/reasons/{reasonId}/endorsement";

    public static string AppealsList(string? sort, string? cursor)
    {
        var query = new List<string>();
        if (!string.IsNullOrEmpty(sort))
        {
            query.Add("sort=" + Uri.EscapeDataString(sort));
        }

        if (!string.IsNullOrEmpty(cursor))
        {
            query.Add("cursor=" + Uri.EscapeDataString(cursor));
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
