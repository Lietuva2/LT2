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
        public const string Sessions = Root + "/sessions";
        public const string Session = Root + "/sessions/{id:guid}";

        public static string SessionById(Guid id) => $"{Sessions}/{id}";
    }

    public static class Admin
    {
        public const string Settings = Prefix + "/admin/settings";
        public const string Setting = Prefix + "/admin/settings/{key}";

        public static string SettingByKey(string key) => $"{Settings}/{Uri.EscapeDataString(key)}";
    }
}

/// <summary>Header names the API and its clients agree on.</summary>
public static class ApiHeaders
{
    /// <summary>Antiforgery request token header, required on every state-changing call.</summary>
    public const string Antiforgery = "X-XSRF-TOKEN";
}
