using System.Data.Common;

namespace Lt2.SharedKernel;

/// <summary>
/// The consent a member gives by switching "Viešas profilis" on (design 8.25.1): it covers the public page, its indexing,
/// being followed and the shown number of followers. Withdrawn by switching the profile off.
/// </summary>
public static class PublicProfileConsent
{
    public const string Kind = "public-profile";

    /// <summary>
    /// The version of the form's text in force. The text is in the client's resources under this version; a new wording gets
    /// a new version, and members with a public profile are asked again.
    /// </summary>
    public const string TextVersion = "2026-11-16";
}

/// <summary>
/// The consent to show the supported party on the public profile (design 8.25.9, owner 2026-11-17): its own box under the
/// party on "Profilis", never implied by "Viešas profilis". Withdrawn by unticking the box, by removing or changing the party
/// and by switching the profile off; switching the profile on again does not give it back.
/// </summary>
public static class PartyPublicConsent
{
    public const string Kind = "party-public";

    /// <summary>The version of the box's text in force (the client's <c>UiText</c> under the same version).</summary>
    public const string TextVersion = "2026-11-17";
}

/// <summary>A member's public profile as other modules see it (design 8.25): never the account's address or anything private.</summary>
/// <param name="UserId">The account, for the module that asked; never in a public answer.</param>
/// <param name="ProfileId">The public id of <c>/nariai/{id}</c>.</param>
/// <param name="DisplayName">The display name (a public profile always has one).</param>
/// <param name="About">"Apie mane", or null when there is none or a moderator hid it.</param>
public sealed record PublicProfileFacts(Guid UserId, string ProfileId, string DisplayName, string? About, DateTimeOffset PublicSince);

/// <summary>What an id under <c>/nariai/</c> is (design 8.25.1): public, retired for good (410), or never issued (404).</summary>
public enum ProfileIdState
{
    Unknown,
    Public,
    Retired,
}

/// <summary>Public profiles (design 8.25), implemented by Identity, which owns them.</summary>
public interface IPublicProfiles
{
    /// <summary>The public profiles among the given accounts, in one read; accounts without one are missing.</summary>
    Task<IReadOnlyDictionary<Guid, PublicProfileFacts>> ForAccountsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>The profiles that are public now among the given ids, in one read; any other id is missing.</summary>
    Task<IReadOnlyDictionary<string, PublicProfileFacts>> PublicByIdsAsync(IReadOnlyCollection<string> profileIds, CancellationToken cancellationToken);

    /// <summary>The profile behind an id, and whether the id is public, retired or unknown.</summary>
    Task<(ProfileIdState State, PublicProfileFacts? Profile)> FindAsync(string profileId, CancellationToken cancellationToken);

    /// <summary>
    /// The party the member supports, only while the profile is public and the member gave <see cref="PartyPublicConsent"/>
    /// (design 8.25.9); null otherwise. For the profile's own page only: never beside a text or in the card.
    /// </summary>
    Task<string?> ShownPartyAsync(string profileId, CancellationToken cancellationToken);
}

/// <summary>
/// Ends every follow of a member's public profile when it is switched off or the account is deleted (design 8.25.1), in the
/// caller's transaction. Implemented by Notifications, which owns follows; no follower is told.
/// </summary>
public interface IMemberFollowEnd
{
    /// <summary>Returns how many follows ended.</summary>
    Task<int> EndAsync(string profileId, DbTransaction transaction, CancellationToken cancellationToken);
}

/// <summary>
/// How many members follow one thing (design 8.11.2, owner 2026-11-16): a number only, counted when asked, never who follows.
/// Implemented by Notifications.
/// </summary>
public interface IFollowerCounts
{
    Task<int> BillAsync(string billNumber, CancellationToken cancellationToken);

    /// <summary>
    /// How many follow each of many bills at once (design 8.3, "Projektai be aprašymo"), by the bill's number as
    /// <see cref="BillAsync"/> counts it; a bill nobody follows is not in the answer.
    /// </summary>
    Task<IReadOnlyDictionary<string, int>> BillsAsync(IReadOnlyCollection<string> billNumbers, CancellationToken cancellationToken);

    Task<int> MpAsync(long seimasPersonId, CancellationToken cancellationToken);

    Task<int> OrganisationAsync(Guid organisationId, CancellationToken cancellationToken);

    Task<int> MemberAsync(string profileId, CancellationToken cancellationToken);
}

/// <summary>The kinds of a member's own texts (design 8.25.2): an argument, or a comment written in their own voice (8.23.2).</summary>
public static class MemberTextKinds
{
    public const string Argument = "argument";

    public const string Comment = "comment";
}

/// <summary>
/// A published text as a member's public page lists it (design 8.25.2): an argument with the side it was written for (public
/// anyway beside it), or a comment in the member's own voice with the kind of text it stands under (<c>reason</c>,
/// <c>statement</c>, <c>response</c>); its bill, when it became public and its text. Nothing about the author's own side, and
/// never a comment written as an organisation or an MP.
/// </summary>
public sealed record MemberTextFacts(Guid Id, string Kind, string BillNumber, string BillTitle, string? Side, string? On, string Text, DateTimeOffset PublishedAt);

/// <summary>One page of a member's published texts, newest first, and whether there are more.</summary>
public sealed record MemberTextsPage(IReadOnlyList<MemberTextFacts> Items, bool More);

/// <summary>A text that became public (an argument or an own-voice comment), for the follow emails about a member (design 8.25.5). No text.</summary>
public sealed record PublishedTextFacts(Guid Id, string Kind, Guid AuthorUserId, string BillNumber, DateTimeOffset PublishedAt);

/// <summary>
/// A member's texts in their own voice (design 8.25.2), implemented by Appeals: published arguments and the comments written as
/// a person (8.23.2) that the bill's page shows. Never "Sutinku" (8.23.10: not listed on the profile).
/// </summary>
public interface IMemberTexts
{
    /// <summary>
    /// The member's shown texts, newest first, the first <paramref name="take"/>: published arguments on bills whose record is
    /// not hidden, and published comments in their own voice whose target is shown on such a bill. Never hidden, waiting,
    /// merged or withdrawn ones.
    /// </summary>
    Task<MemberTextsPage> PublishedAsync(Guid userId, int take, CancellationToken cancellationToken);

    /// <summary>
    /// Of the given authors, the texts that first became public at or after <paramref name="since"/> and are shown now, as
    /// <see cref="PublishedAsync"/> says; oldest first.
    /// </summary>
    Task<IReadOnlyList<PublishedTextFacts>> PublishedSinceAsync(IReadOnlyCollection<Guid> authors, DateTimeOffset since, CancellationToken cancellationToken);
}

/// <summary>
/// A Lithuanian personal code (asmens kodas): 11 digits, the first 1–6 (century and sex), a valid birth month and day, and the
/// official check digit. The one definition, for pre-screening (Appeals) and "Apie mane" (Identity, design 8.25.3).
/// </summary>
public static partial class PersonalCodes
{
    public static bool Contains(string text) => ElevenDigits().Matches(text).Any(m => Is(m.Value));

    public static bool Is(string digits)
    {
        ArgumentNullException.ThrowIfNull(digits);
        if (digits.Length != 11 || !digits.All(char.IsAsciiDigit) || digits[0] is < '1' or > '6')
        {
            return false;
        }

        var d = digits.Select(c => c - '0').ToArray();
        var month = d[3] * 10 + d[4];
        var day = d[5] * 10 + d[6];
        if (month is < 1 or > 12 || day is < 1 or > 31)
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < 10; i++)
        {
            sum += d[i] * (i % 9 + 1);
        }

        var check = sum % 11;
        if (check == 10)
        {
            sum = 0;
            for (var i = 0; i < 10; i++)
            {
                sum += d[i] * ((i + 2) % 9 + 1);
            }

            check = sum % 11;
            if (check == 10)
            {
                check = 0;
            }
        }

        return check == d[10];
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"(?<!\d)\d{11}(?!\d)", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex ElevenDigits();
}
