using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Lt2.Appeals.Domain;

/// <summary>
/// Design 8.14.14 (stage A) as pure functions: what an organisation, its profile, its confirmation by email domain and its
/// statements must be. Each check returns the error code a request gets when it breaks the rule, or <c>null</c>.
/// </summary>
internal static partial class OrganisationRules
{
    public const int NameMinLength = 3;
    public const int NameMaxLength = 120;
    public const int DescriptionMaxLength = 500;
    public const int WebsiteMaxLength = 200;
    public const int StatementMaxLength = 1500;
    public const int LinkMaxLength = 500;
    public const int GoalMax = 1_000_000;
    public const int SlugMaxLength = 60;

    /// <summary>Representatives one organisation may have at once (design 8.14.14).</summary>
    public const int RepresentativesMax = 10;

    /// <summary>How long a representative's one-time link stays valid (design 8.14.14).</summary>
    public static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(7);

    /// <summary>A name is one line: plain text with every run of white space made one space.</summary>
    public static string NormaliseName(string? name) =>
        string.Join(' ', AppealRules.NormaliseText(name).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The name as two organisations are compared: without case (design 8.14.14).</summary>
    public static string NameKey(string? name) => NormaliseName(name).ToLowerInvariant();

    public static string? CheckName(string? name)
    {
        var normalised = NormaliseName(name);
        if (normalised.Length == 0)
        {
            return "organisation.name-empty";
        }

        return normalised.Length is < NameMinLength or > NameMaxLength ? "organisation.name-length" : null;
    }

    /// <summary>Optional plain text, at most 500 characters.</summary>
    public static string? CheckDescription(string? description) =>
        AppealRules.NormaliseText(description).Length > DescriptionMaxLength ? "organisation.description-too-long" : null;

    /// <summary>Optional; an https address of at most 200 characters (design 8.14.14). Never fetched.</summary>
    public static string? CheckWebsite(string? website) =>
        string.IsNullOrWhiteSpace(website) || HttpsAddress.IsAcceptable(website, WebsiteMaxLength) ? null : "organisation.website-invalid";

    /// <summary>Optional; 7 to 9 digits (design 8.14.14).</summary>
    public static string? CheckLegalCode(string? code)
    {
        var trimmed = code?.Trim() ?? "";
        return trimmed.Length == 0 || (trimmed.Length is >= 7 and <= 9 && trimmed.All(char.IsAsciiDigit)) ? null : "organisation.legal-code-invalid";
    }

    /// <summary>An optional field as stored: trimmed, or null when empty.</summary>
    public static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>At most <c>Organisations:PerAccount</c> organisations an account registered and has not closed.</summary>
    public static string? CheckRegistering(int registeredNotClosed, int limit) =>
        registeredNotClosed >= limit ? "organisation.limit" : null;

    /// <summary>
    /// The address <c>/o/{slug}</c> from the name: Lithuanian letters without their marks, lower case, words joined by
    /// hyphens, at most 60 characters. Empty when nothing of the name is a letter or a digit (the caller then uses a fallback).
    /// </summary>
    public static string Slug(string name)
    {
        var builder = new StringBuilder();
        var hyphen = false;
        foreach (var c in NormaliseName(name).ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (hyphen && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(c);
                hyphen = false;
            }
            else
            {
                hyphen = true;
            }
        }

        var slug = builder.ToString();
        return slug.Length <= SlugMaxLength ? slug : slug[..SlugMaxLength].TrimEnd('-');
    }

    /// <summary>The <paramref name="n"/>-th candidate for a slug that is taken: <c>name</c>, <c>name-2</c>, <c>name-3</c> …</summary>
    public static string SlugCandidate(string slug, int n) => n <= 1 ? slug : $"{slug}-{n.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// The host a website is on, as a confirmation compares it: the IDN (punycode) form in lower case, without a trailing
    /// dot and without a leading <c>www.</c>. Null for an address that is not acceptable.
    /// </summary>
    public static string? WebsiteHost(string? website)
    {
        if (string.IsNullOrWhiteSpace(website) || !HttpsAddress.IsAcceptable(website, WebsiteMaxLength)
            || !Uri.TryCreate(website.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        var host = uri.IdnHost.ToLowerInvariant().TrimEnd('.');
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    /// <summary>
    /// The domain of an email address, in IDN (punycode) form and lower case, or null when the address is not one plain
    /// mailbox: exactly one <c>@</c>, no quoting, comments, routing (<c>%</c>, <c>!</c>), address literals or white
    /// space, and a domain of at least two valid labels.
    /// </summary>
    public static string? EmailDomain(string? address)
    {
        var value = address?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > 254 || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            return null;
        }

        var at = value.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at != value.LastIndexOf('@') || at == value.Length - 1 || at > 64)
        {
            return null;
        }

        var local = value[..at];
        if (local.IndexOfAny(['"', '\\', '(', ')', ',', ';', ':', '<', '>', '[', ']', '%', '!']) >= 0
            || local.StartsWith('.') || local.EndsWith('.') || local.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        string ascii;
        try
        {
            ascii = new IdnMapping { AllowUnassigned = false, UseStd3AsciiRules = true }.GetAscii(value[(at + 1)..]).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return null;
        }

        var labels = ascii.Split('.');
        return labels.Length >= 2 && ascii.Length <= 253 && labels.All(IsLabel) && !labels[^1].All(char.IsAsciiDigit) ? ascii : null;
    }

    private static bool IsLabel(string label) =>
        label.Length is >= 1 and <= 63 && !label.StartsWith('-') && !label.EndsWith('-') && label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    /// <summary>The domain is the host itself or a name under it: <c>lgs.lt</c> or <c>mail.lgs.lt</c>, never <c>evil-lgs.lt</c>.</summary>
    public static bool IsAtOrUnder(string domain, string host) =>
        domain.Equals(host, StringComparison.Ordinal) || domain.EndsWith("." + host, StringComparison.Ordinal);

    /// <summary>A domain on the fixed list of public mail services, or under one.</summary>
    public static bool IsPublicMail(string domain) => PublicMailDomains.Any(d => IsAtOrUnder(domain, d));

    /// <summary>
    /// A website on a public mail service or on a shared platform (social networks, site builders, code hosts): it cannot be
    /// confirmed by domain, since many unrelated people have addresses or pages there.
    /// </summary>
    public static bool IsSharedHost(string host) => IsPublicMail(host) || SharedPlatforms.Any(d => IsAtOrUnder(host, d));

    /// <summary>
    /// Whether a code may be sent to <paramref name="address"/> to confirm an organisation with <paramref name="website"/>
    /// (design 8.14.14): the address must be on the website's host (without <c>www.</c>) or a name under it, the website not
    /// on a public mail service or shared platform, and the address not at a public mail service. Returns the error code,
    /// or null with the domain that will be stored.
    /// </summary>
    public static (string? Error, string? Domain) CheckConfirmationAddress(string? website, string? address)
    {
        var host = WebsiteHost(website);
        if (host is null)
        {
            return ("confirm.no-website", null);
        }

        if (IsSharedHost(host) || !host.Contains('.', StringComparison.Ordinal))
        {
            return ("confirm.shared-website", null);
        }

        var domain = EmailDomain(address);
        if (domain is null)
        {
            return ("confirm.address-invalid", null);
        }

        if (IsPublicMail(domain))
        {
            return ("confirm.public-mail", null);
        }

        return IsAtOrUnder(domain, host) ? (null, host) : ("confirm.wrong-domain", null);
    }

    /// <summary>
    /// Whether the organisation may be confirmed by a code now: it is active, not already confirmed, and no moderator has
    /// withdrawn a confirmation of it (after that only a moderator confirms it).
    /// </summary>
    public static string? CheckDomainConfirmation(string status, string? confirmedBy, bool domainBarred)
    {
        if (status != OrganisationStatuses.Active)
        {
            return "organisation.not-active";
        }

        if (domainBarred)
        {
            return "confirm.barred";
        }

        return confirmedBy is null ? null : "confirm.already";
    }

    /// <summary>
    /// A changed website removes a confirmation by domain unless the new host is the confirmed domain (design 8.14.14);
    /// a moderator's confirmation stays.
    /// </summary>
    public static bool KeepsConfirmation(string? confirmedBy, string? confirmedDomain, string? newWebsite) =>
        confirmedBy != ConfirmationKinds.Domain || (confirmedDomain is not null && WebsiteHost(newWebsite) == confirmedDomain);

    /// <summary>A representative acts for the organisation only while it is active: a blocked one cannot publish or change.</summary>
    public static string? CheckActive(string status) => status switch
    {
        OrganisationStatuses.Active => null,
        OrganisationStatuses.Blocked => "organisation.blocked",
        _ => "organisation.closed",
    };

    /// <summary>A new representative while there are fewer than ten.</summary>
    public static string? CheckNewRepresentative(int activeRepresentatives) =>
        activeRepresentatives >= RepresentativesMax ? "organisation.representatives-full" : null;

    /// <summary>The last representative cannot leave; they can only close the organisation (design 8.14.14).</summary>
    public static string? CheckLeaving(int activeRepresentatives) =>
        activeRepresentatives <= 1 ? "organisation.last-representative" : null;

    /// <summary>Only the last representative closes the organisation, and not while it is blocked (a block is not undone by closing).</summary>
    public static string? CheckClosing(string status, int activeRepresentatives)
    {
        if (status == OrganisationStatuses.Blocked)
        {
            return "organisation.blocked";
        }

        if (status == OrganisationStatuses.Closed)
        {
            return "organisation.closed";
        }

        return activeRepresentatives > 1 ? "organisation.not-last" : null;
    }

    /// <summary>
    /// A statement (design 8.14.3, 8.14.14): a stance <c>for</c>, <c>against</c> or <c>remarks</c>; 1 to 1 500 characters of
    /// plain text; an optional https link of at most 500 characters; with <c>for</c> or <c>against</c> an optional goal of 1
    /// to 1 000 000 participants, and no goal with remarks.
    /// </summary>
    public static string? CheckStatement(string? stance, string? text, string? link, int? goal)
    {
        if (stance is null || !Stances.All.Contains(stance))
        {
            return "statement.stance-invalid";
        }

        var normalised = AppealRules.NormaliseText(text);
        if (normalised.Length == 0)
        {
            return "statement.empty";
        }

        if (normalised.Length > StatementMaxLength)
        {
            return "statement.too-long";
        }

        if (!string.IsNullOrWhiteSpace(link) && !HttpsAddress.IsAcceptable(link, LinkMaxLength))
        {
            return "statement.link-invalid";
        }

        if (goal is null)
        {
            return null;
        }

        if (stance == Stances.Remarks)
        {
            return "statement.goal-with-remarks";
        }

        return goal is < 1 or > GoalMax ? "statement.goal-invalid" : null;
    }

    /// <summary>
    /// A statement can be published, changed or withdrawn only on an active bill whose record is open or not yet made
    /// (design 8.14.14): a hidden record (taking part closed) accepts none, and a closed bill freezes it.
    /// </summary>
    public static string? CheckBillOpen(string? recordStatus, bool billFinished) =>
        billFinished || recordStatus is not (null or AppealStatuses.Open) ? "statement.bill-not-open" : null;

    /// <summary>
    /// Shown anywhere public: the statement is published and its organisation active. A blocked or closed organisation's
    /// statements are shown nowhere; lifting a block shows again exactly the published ones, nothing withdrawn or hidden.
    /// </summary>
    public static bool IsShown(string organisationStatus, string statementStatus) =>
        organisationStatus == OrganisationStatuses.Active && statementStatus == StatementStatuses.Published;

    /// <summary>
    /// The goal's progress (design 8.14.5): the participants on the organisation's side as the page counts them. Remarks
    /// and a statement without a goal have none.
    /// </summary>
    public static int? Progress(string stance, int? goal, int forCount, int againstCount) => goal is null ? null : stance switch
    {
        Stances.For => forCount,
        Stances.Against => againstCount,
        _ => null,
    };
}

/// <summary>
/// The links users give (an organisation's website, a statement's link): <c>https</c> only, a DNS name with at least one
/// dot, no user name or password, no white space, within a length. The same rule as <c>UserLink</c> in the API contracts,
/// which the forms and the endpoints apply; a unit test holds the two together. Never fetched by the server.
/// </summary>
internal static class HttpsAddress
{
    public static bool IsAcceptable(string? value, int maxLength)
    {
        var address = value?.Trim();
        if (string.IsNullOrEmpty(address) || address.Length > maxLength || address.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '\\'))
        {
            return false;
        }

        const string Prefix = "https://";
        if (!address.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var authority = address[Prefix.Length..];
        var end = authority.IndexOfAny(['/', '?', '#']);
        if ((end < 0 ? authority : authority[..end]).Contains('@', StringComparison.Ordinal))
        {
            return false;
        }

        return Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0
            && uri.HostNameType == UriHostNameType.Dns && uri.IdnHost.Trim('.').Contains('.', StringComparison.Ordinal);
    }

    /// <summary>The host as a reader sees it beside the link (design 8.14.14: "shown with its host"), without <c>www.</c>.</summary>
    public static string Host(string address)
    {
        var host = Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : address;
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }
}

/// <summary>
/// The confirmation codes (design 8.14.14) follow the sign-in codes' rules (design 6.2): six digits, ten minutes, five
/// attempts, single use, a new code replaces the earlier one, stored only as a hash salted with the code's own id; and the
/// settings <c>SignIn:CodesPerHour</c> and <c>SignIn:CodesPerDay</c> bound how many one organisation may be sent.
/// </summary>
internal static class OrganisationCodeRules
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public static string Generate() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    public static byte[] Hash(Guid codeId, string code) => SHA256.HashData(Encoding.UTF8.GetBytes($"{codeId:N}:{code}"));

    public static bool MayIssue(int issuedInLastHour, int issuedInLastDay, int perHour, int perDay) =>
        issuedInLastHour < perHour && issuedInLastDay < perDay;

    /// <summary>Only the newest unused code counts; a used, expired or exhausted one never matches, even with the right digits.</summary>
    public static bool Matches(OrganisationCode code, string? input, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (code.ConsumedAt is not null || now >= code.ExpiresAt || code.FailedAttempts >= MaxAttempts)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(code.CodeHash, Hash(code.Id, (input ?? "").Trim()));
    }
}

/// <summary>A representative's one-time link (design 8.14.14): a random token, only its hash stored.</summary>
internal static class InviteTokens
{
    public static string New() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token ?? ""));

    /// <summary>A link works once, within seven days.</summary>
    public static bool IsUsable(DateTimeOffset expiresAt, DateTimeOffset? usedAt, DateTimeOffset now) => usedAt is null && now < expiresAt;
}

/// <summary>
/// The fixed lists behind confirmation by domain (design 8.14.14). A website on one of these, or an address at a public mail
/// service, confirms nothing; such an organisation asks a moderator.
/// </summary>
internal static partial class OrganisationRules
{
    /// <summary>Public mail services, international and Lithuanian: an address there proves no organisation's domain.</summary>
    public static readonly IReadOnlyList<string> PublicMailDomains =
    [
        "gmail.com", "googlemail.com", "outlook.com", "outlook.lt", "hotmail.com", "hotmail.lt", "hotmail.co.uk", "live.com", "live.lt", "msn.com",
        "yahoo.com", "yahoo.co.uk", "yahoo.de", "ymail.com", "rocketmail.com", "aol.com", "icloud.com", "me.com", "mac.com",
        "proton.me", "protonmail.com", "protonmail.ch", "pm.me", "tutanota.com", "tuta.io", "gmx.com", "gmx.net", "gmx.de", "web.de",
        "yandex.com", "yandex.ru", "ya.ru", "mail.ru", "bk.ru", "list.ru", "inbox.ru", "rambler.ru", "zoho.com", "fastmail.com",
        "hey.com", "mail.com", "email.com", "seznam.cz", "wp.pl", "o2.pl", "onet.pl", "interia.pl",
        "inbox.lt", "one.lt", "mail.lt", "takas.lt", "zebra.lt", "omni.lt",
        "inbox.lv", "inbox.eu", "mail.ee",
    ];

    /// <summary>
    /// Shared platforms: social networks, site builders, blog and code hosts, where anyone can have a page. A website there
    /// cannot be confirmed by domain.
    /// </summary>
    public static readonly IReadOnlyList<string> SharedPlatforms =
    [
        "facebook.com", "fb.com", "instagram.com", "linkedin.com", "x.com", "twitter.com", "youtube.com", "tiktok.com", "threads.net",
        "medium.com", "substack.com", "tumblr.com", "blogspot.com", "blogger.com", "wordpress.com", "wix.com", "wixsite.com",
        "squarespace.com", "weebly.com", "webnode.com", "webnode.lt", "jimdo.com", "jimdosite.com", "site123.me", "carrd.co",
        "notion.site", "linktr.ee", "google.com", "sites.google.com", "github.io", "github.com", "gitlab.io", "gitlab.com",
        "netlify.app", "vercel.app", "pages.dev", "web.app", "firebaseapp.com", "herokuapp.com", "azurewebsites.net",
    ];
}
