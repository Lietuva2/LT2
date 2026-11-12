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
    public const string Methodology = ApiRoutes.Prefix + "/meta/methodology";

    public static string MpBySlug(string slug) => $"{Mps}/{Uri.EscapeDataString(slug)}";

    public static string MpVotesBySlug(string slug) => $"{MpBySlug(slug)}/votes";

    public static string FactionBySlug(string slug) => $"{Factions}/{Uri.EscapeDataString(slug)}";

    public static string CommitteeBySlug(string slug) => $"{Committees}/{Uri.EscapeDataString(slug)}";

    public static string BillByNumber(string number) => $"{Bills}/{Uri.EscapeDataString(number)}";

    public static string VoteById(long id) => $"{ApiRoutes.Prefix}/votes/{id.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>Header names the API and its clients agree on.</summary>
public static class ApiHeaders
{
    /// <summary>Antiforgery request token header, required on every state-changing call.</summary>
    public const string Antiforgery = "X-XSRF-TOKEN";
}
