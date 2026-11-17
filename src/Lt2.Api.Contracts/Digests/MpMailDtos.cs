using FluentValidation;
using Lt2.Api.Contracts.Appeals;
using Microsoft.Extensions.Localization;

namespace Lt2.Api.Contracts.Digests;

// Design 8.19.4 to 8.19.7: the MP's choices without an account (token routes), the same choice for a linked account, the
// introduction letter and the suppression list. Frequency default | weekly | before-vote | stopped. A token answers the same for
// an unknown and a wrong one (404 mp-mail.not-found). Nothing here is in a public answer: the MP's page alone shows its choice.

/// <summary>The routes of design 8.19.7 for MP mail, and the pages they serve.</summary>
public static class MpMailRoutes
{
    public const string Prefix = ApiRoutes.Prefix + "/mp-mail";

    /// <summary><c>GET</c> the MP's name and choice; <c>PUT</c> the frequency.</summary>
    public const string Token = Prefix + "/{token}";

    /// <summary>
    /// <c>POST</c>, the one-click stop of RFC 8058 (<c>List-Unsubscribe-Post</c>): the one write that needs no same-origin check,
    /// mapped outside the API group's antiforgery filter on purpose; it can only set "Nesiųsti".
    /// </summary>
    public const string Stop = Token + "/stop";

    /// <summary><c>POST</c>: a new link to the official address; the old one stops working.</summary>
    public const string Renew = Token + "/renew";

    /// <summary><c>PUT</c> asks for an extra recipient; <c>DELETE</c> removes it.</summary>
    public const string Extra = Token + "/extra";

    /// <summary><c>POST</c>: the official address confirms the extra recipient (the token in the body).</summary>
    public const string ConfirmExtra = Prefix + "/extra/confirm";

    /// <summary><c>POST</c>: "Nebegauti kopijų" from a copy (the token in the body).</summary>
    public const string StopCopies = Prefix + "/extra/stop";

    /// <summary><c>GET</c> / <c>PUT</c>: the same choice for a linked account in a passkey session (design 8.19.4).</summary>
    public const string Mine = ApiRoutes.Prefix + "/me/mp/mail";

    /// <summary>
    /// <c>GET</c> / <c>PUT</c>: an office member's own choice of copies of the MP's summaries (design 8.19.3, owner 2026-11-17),
    /// independent of the MP's; any session, since it is the member's own mail.
    /// </summary>
    public const string OfficeCopies = ApiRoutes.Prefix + "/me/mp/office/copies";

    /// <summary>The MP's page of choices, from the link in every email.</summary>
    public const string PreferencesPage = "/suvestines/nustatymai";

    /// <summary>The page that confirms an extra recipient, from the link sent to the official address.</summary>
    public const string ConfirmPage = "/suvestines/nustatymai/patvirtinti";

    /// <summary>The page a copy's "Nebegauti kopijų" opens.</summary>
    public const string CopiesPage = "/suvestines/kopijos";

    public static string TokenOf(string token) => Prefix + "/" + Uri.EscapeDataString(token);

    public static string StopOf(string token) => TokenOf(token) + "/stop";

    public static string RenewOf(string token) => TokenOf(token) + "/renew";

    public static string ExtraOf(string token) => TokenOf(token) + "/extra";

    public static string PreferencesPageOf(string token) => PreferencesPage + "/" + Uri.EscapeDataString(token);
}

/// <summary>
/// The MP's choice as the MP's own page shows it. <see cref="ExtraAddress"/> only on the token page (the linked account's answer
/// leaves it out), with <see cref="ExtraConfirmed"/> false while the official address has not confirmed it.
/// </summary>
public sealed record MpMailChoiceResponse(string Slug, string FirstName, string LastName, string Frequency, string? ExtraAddress, bool ExtraConfirmed);

/// <summary>An office member's own choice of copies of the MP's summaries (design 8.19.3): the MP by name and the frequency.</summary>
public sealed record MpOfficeCopiesResponse(string Slug, string FirstName, string LastName, string Frequency);

public sealed record MpMailFrequencyRequest
{
    public string Frequency { get; init; } = "";
}

public sealed record MpMailExtraRequest
{
    public string Address { get; init; } = "";
}

/// <summary>A token read from a link and sent in the body (the confirmation and a copy's stop).</summary>
public sealed record MpMailTokenRequest
{
    public string Token { get; init; } = "";
}

public static class MpMailFrequencies
{
    public const string Default = "default";
    public const string Weekly = "weekly";
    public const string BeforeVote = "before-vote";
    public const string Stopped = "stopped";

    public static readonly IReadOnlyList<string> All = [Default, Weekly, BeforeVote, Stopped];
}

public sealed class MpMailFrequencyRequestValidator : AbstractValidator<MpMailFrequencyRequest>
{
    public MpMailFrequencyRequestValidator(IStringLocalizer<ValidationMessages> l) =>
        RuleFor(x => x.Frequency).Must(f => MpMailFrequencies.All.Contains(f)).WithMessage(l["MpMail.Frequency"]);
}

public sealed class MpMailExtraRequestValidator : AbstractValidator<MpMailExtraRequest>
{
    public MpMailExtraRequestValidator(IStringLocalizer<ValidationMessages> l) =>
        RuleFor(x => x.Address).Must(a => !string.IsNullOrWhiteSpace(a) && a.Trim().Length <= 254 && a.Contains('@', StringComparison.Ordinal))
            .WithMessage(l["MpMail.Address"]);
}

public sealed class MpMailTokenRequestValidator : AbstractValidator<MpMailTokenRequest>
{
    public MpMailTokenRequestValidator(IStringLocalizer<ValidationMessages> l) =>
        RuleFor(x => x.Token).Cascade(CascadeMode.Stop).NotEmpty().WithMessage(l["MpLink.Required"]).MaximumLength(100).WithMessage(l["MpLink.Required"]);
}

/// <summary>The MP's choices (design 8.19.4): by the link's token, and for a linked account.</summary>
public interface IMpMailApi
{
    Task<ApiResult<MpMailChoiceResponse>> GetAsync(string token, CancellationToken cancellationToken = default);

    Task<ApiResult<MpMailChoiceResponse>> SetFrequencyAsync(string token, MpMailFrequencyRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> RenewAsync(string token, CancellationToken cancellationToken = default);

    Task<ApiResult<MpMailChoiceResponse>> AskExtraAsync(string token, MpMailExtraRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult<MpMailChoiceResponse>> RemoveExtraAsync(string token, CancellationToken cancellationToken = default);

    Task<ApiResult<MpMailChoiceResponse>> ConfirmExtraAsync(MpMailTokenRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> StopCopiesAsync(MpMailTokenRequest request, CancellationToken cancellationToken = default);

    /// <summary>The linked account's own MP's choice; <c>mp.not-linked</c> without a link.</summary>
    Task<ApiResult<MpMailChoiceResponse>> GetMineAsync(CancellationToken cancellationToken = default);

    /// <summary>Changes it, in a passkey session (<c>mp.passkey-required</c> otherwise).</summary>
    Task<ApiResult<MpMailChoiceResponse>> SetMineAsync(MpMailFrequencyRequest request, CancellationToken cancellationToken = default);

    /// <summary>An office member's own copies (design 8.19.3); <c>mp.not-office</c> for anyone but an office member.</summary>
    Task<ApiResult<MpOfficeCopiesResponse>> GetOfficeCopiesAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<MpOfficeCopiesResponse>> SetOfficeCopiesAsync(MpMailFrequencyRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Administrators' routes and pages for MP mail (design 8.19.5, 8.19.6).</summary>
public static class MpSendingAdminRoutes
{
    /// <summary><c>GET</c> the letter's state; <c>PUT</c> saves the letter.</summary>
    public const string Introduction = ApiRoutes.Prefix + "/admin/introduction";

    /// <summary><c>POST</c>: a test copy to the administrator's own address.</summary>
    public const string IntroductionTest = Introduction + "/test";

    /// <summary><c>POST</c>, fresh passkey confirmation: sends the letter to the MPs who have had none.</summary>
    public const string IntroductionSend = Introduction + "/send";

    /// <summary><c>GET</c> the list; <c>POST</c> adds an address; <c>DELETE</c> removes an entry.</summary>
    public const string Suppression = ApiRoutes.Prefix + "/admin/suppression";

    public const string IntroductionPage = "/administravimas/pristatymas";

    public const string SuppressionPage = "/administravimas/nesiunciama";
}

/// <summary>One version of the introduction letter, for the administrator.</summary>
public sealed record IntroductionVersionResponse(int Version, string Subject, string Text, string SignerName, string SignerRole, DateTimeOffset WrittenAt, DateTimeOffset? SentAt);

/// <summary>
/// The screen "Pristatymo laiškas": the version being written or last sent, the versions sent, how many MPs would get it now, and
/// whether MP mail may leave this deployment (invented data and mail not caught: no).
/// </summary>
public sealed record IntroductionResponse(IntroductionVersionResponse? Current, IReadOnlyList<IntroductionVersionResponse> Sent, int WouldGoTo, bool MailMayLeave);

public sealed record IntroductionRequest
{
    public string Subject { get; init; } = "";

    public string Text { get; init; } = "";

    public string SignerName { get; init; } = "";

    public string SignerRole { get; init; } = "";
}

/// <summary>How many MPs the letter went to.</summary>
public sealed record IntroductionSentResponse(int Mps);

public sealed class IntroductionRequestValidator : AbstractValidator<IntroductionRequest>
{
    public IntroductionRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.Subject).Must(t => !string.IsNullOrWhiteSpace(t) && t.Trim().Length <= 200).WithMessage(l["Introduction.Subject"]);
        RuleFor(x => x.Text).Must(t => !string.IsNullOrWhiteSpace(t) && t.Trim().Length <= 10000).WithMessage(l["Introduction.Text"]);
        RuleFor(x => x.SignerName).Must(t => !string.IsNullOrWhiteSpace(t) && t.Trim().Length <= 200).WithMessage(l["Introduction.Signer"]);
        RuleFor(x => x.SignerRole).Must(t => !string.IsNullOrWhiteSpace(t) && t.Trim().Length <= 200).WithMessage(l["Introduction.Signer"]);
    }
}

/// <summary>A sitting MP whose official address is suppressed, with the entry's reason (<c>bounced</c>, <c>complaint</c>, <c>staff</c>) and date.</summary>
public sealed record SuppressedMpResponse(Guid Id, string Slug, string FirstName, string LastName, string Reason, DateTimeOffset Since);

/// <summary>The screen "Nesiunčiama": the sitting MPs on the list, and how many other entries (never shown).</summary>
public sealed record SuppressionResponse(IReadOnlyList<SuppressedMpResponse> Mps, int Others);

public sealed record SuppressRequest
{
    public string Address { get; init; } = "";

    public string Statement { get; init; } = "";
}

/// <summary>Removes an entry by its id (a listed MP) or by the address typed (any other entry), with a statement.</summary>
public sealed record UnsuppressRequest
{
    public Guid? EntryId { get; init; }

    public string? Address { get; init; }

    public string Statement { get; init; } = "";
}

public sealed class SuppressRequestValidator : AbstractValidator<SuppressRequest>
{
    public SuppressRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.Address).Must(a => !string.IsNullOrWhiteSpace(a) && a.Trim().Length <= 254 && a.Contains('@', StringComparison.Ordinal))
            .WithMessage(l["MpMail.Address"]);
        RuleFor(x => (string?)x.Statement).Statement(l, required: true).OverridePropertyName("statement");
    }
}

public sealed class UnsuppressRequestValidator : AbstractValidator<UnsuppressRequest>
{
    public UnsuppressRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x).Must(x => x.EntryId is not null || !string.IsNullOrWhiteSpace(x.Address)).WithMessage(l["MpMail.Address"]).OverridePropertyName("address");
        RuleFor(x => (string?)x.Statement).Statement(l, required: true).OverridePropertyName("statement");
    }
}

/// <summary>The administrators' screens "Pristatymo laiškas" and "Nesiunčiama" (design 8.19.5, 8.19.6).</summary>
public interface IMpSendingAdminApi
{
    Task<ApiResult<IntroductionResponse>> GetIntroductionAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<IntroductionVersionResponse>> SaveIntroductionAsync(IntroductionRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> SendIntroductionTestAsync(CancellationToken cancellationToken = default);

    /// <summary>Needs a fresh passkey confirmation (<c>passkey.confirmation-required</c>).</summary>
    Task<ApiResult<IntroductionSentResponse>> SendIntroductionAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<SuppressionResponse>> GetSuppressionAsync(CancellationToken cancellationToken = default);

    Task<ApiResult> SuppressAsync(SuppressRequest request, CancellationToken cancellationToken = default);

    Task<ApiResult> UnsuppressAsync(UnsuppressRequest request, CancellationToken cancellationToken = default);
}
