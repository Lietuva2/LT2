namespace Lt2.SharedKernel;

/// <summary>Application roles (design 6.3).</summary>
public static class Roles
{
    public const string Member = "member";
    public const string Moderator = "moderator";
    public const string Administrator = "administrator";

    public static readonly IReadOnlyList<string> All = [Member, Moderator, Administrator];

    public static readonly IReadOnlyList<string> Staff = [Moderator, Administrator];
}

/// <summary>Values of the <c>amr</c> claim (RFC 8176) this application issues.</summary>
public static class AuthenticationMethods
{
    public const string ClaimType = "amr";

    /// <summary>One-time code sent by email.</summary>
    public const string EmailCode = "otp";

    /// <summary>Passkey: proof of possession of a key, with user verification.</summary>
    public const string Passkey = "pop";
}
