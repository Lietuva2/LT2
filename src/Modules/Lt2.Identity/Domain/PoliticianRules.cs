using System.Security.Cryptography;
using System.Text;

namespace Lt2.Identity.Domain;

/// <summary>Why a presented token does not work. Every one of them gets the same answer (design 8.5.2).</summary>
internal enum PoliticianTokenCheck
{
    Valid,
    Unknown,
    WrongKind,
    Used,
    Expired,
    OtherAccount,
}

/// <summary>
/// The rules of an MP's link (design 8.5.2 to 8.5.4): the one-time tokens of the take-over link and of an office member's
/// invitation, their limits, and who may do what. Pure functions; the handlers read the facts from the database.
/// </summary>
internal static class PoliticianRules
{
    /// <summary>A take-over link is good for 24 hours (design 8.5.2).</summary>
    public static readonly TimeSpan TakeoverLifetime = TimeSpan.FromHours(24);

    /// <summary>At most three take-over links a day per MP (design 8.5.2), counted over the last 24 hours.</summary>
    public const int TakeoverLinksPerDay = 3;

    /// <summary>An office member's invitation is good for seven days (design 8.5.3).</summary>
    public static readonly TimeSpan OfficeInvitationLifetime = TimeSpan.FromDays(7);

    /// <summary>At most five office members per MP (design 8.5.3).</summary>
    public const int OfficeMembersPerMp = 5;

    /// <summary>A long random token: 32 bytes, base64url, 43 characters. It cannot be guessed (design 8.5.2).</summary>
    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>Only the hash is stored.</summary>
    public static byte[] Hash(string? token) => SHA256.HashData(Encoding.UTF8.GetBytes((token ?? "").Trim()));

    /// <summary>
    /// A token presented for <paramref name="kind"/>: known, of that kind, unused, unexpired and, for a take-over link,
    /// made for <paramref name="userId"/> (an office invitation is for whoever the MP's account gives it to).
    /// </summary>
    public static PoliticianTokenCheck Check(PoliticianToken? token, string kind, Guid userId, DateTimeOffset now) =>
        token is null ? PoliticianTokenCheck.Unknown
        : token.Kind != kind ? PoliticianTokenCheck.WrongKind
        : token.UsedAt is not null ? PoliticianTokenCheck.Used
        : now >= token.ExpiresAt ? PoliticianTokenCheck.Expired
        : kind == PoliticianTokenKinds.Takeover && token.UserId != userId ? PoliticianTokenCheck.OtherAccount
        : PoliticianTokenCheck.Valid;

    /// <summary>Whether another take-over link may be sent for an MP that had <paramref name="sentInLastDay"/> in the last 24 hours.</summary>
    public static bool MaySendTakeoverLink(int sentInLastDay) => sentInLastDay < TakeoverLinksPerDay;

    /// <summary>Whether the MP's office may take one more member, with <paramref name="members"/> now.</summary>
    public static bool MayAddOfficeMember(int members) => members < OfficeMembersPerMp;

    /// <summary>
    /// Speaking for the MP (design 8.5.4: a response, office members, giving the profile up) needs a passkey session; a
    /// session signed in by an emailed code can only read.
    /// </summary>
    public static bool MaySpeakForMp(bool linked, bool passkeySession) => linked && passkeySession;

    /// <summary>
    /// Adding or removing a passkey on a linked account (the MP's or an office member's, design 8.5.2, 8.5.3) needs a passkey
    /// session; the take-over link is the other way, and it has its own request. An account without a link: no rule here.
    /// </summary>
    public static bool MayChangePasskeys(bool linked, bool passkeySession) => !linked || passkeySession;
}
