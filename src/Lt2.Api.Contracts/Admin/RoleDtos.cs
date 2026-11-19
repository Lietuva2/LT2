using FluentValidation;
using Lt2.Api.Contracts.Appeals;
using Lt2.Api.Contracts.Auth;
using Microsoft.Extensions.Localization;

namespace Lt2.Api.Contracts.Admin;

/// <summary>Grants a staff role (<c>moderator</c> or <c>administrator</c>) to the account with this address (design 6.2).</summary>
public sealed record GrantRoleRequest
{
    public string Email { get; init; } = "";

    public string Role { get; init; } = "";
}

/// <summary>
/// The one-time activation code, shown once: the administrator passes it on directly, never by email. It is valid until
/// <see cref="ExpiresAt"/> and works only in a passkey session.
/// </summary>
public sealed record GrantRoleResponse
{
    public string ActivationCode { get; init; } = "";

    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>Resets the account with this address: its passkeys are removed and its staff roles are inactive until a new activation.</summary>
public sealed record ResetAccountRequest
{
    public string Email { get; init; } = "";
}

/// <summary>
/// Locks or unlocks the account with this address (design 8.3): a locked account cannot sign in; the statement is required, audited
/// and emailed to the person.
/// </summary>
public sealed record AccountLockRequest
{
    public string Email { get; init; } = "";

    public string Statement { get; init; } = "";
}

public sealed class AccountLockRequestValidator : AbstractValidator<AccountLockRequest>
{
    public AccountLockRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.Email).ValidEmail(l);
        RuleFor(x => (string?)x.Statement).Statement(l, required: true).OverridePropertyName("statement");
    }
}

public sealed class GrantRoleRequestValidator : AbstractValidator<GrantRoleRequest>
{
    public GrantRoleRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.Email).ValidEmail(l);
        RuleFor(x => x.Role).NotEmpty().WithMessage(l["Role.Required"]);
    }
}

public sealed class ResetAccountRequestValidator : AbstractValidator<ResetAccountRequest>
{
    public ResetAccountRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.Email).ValidEmail(l);
    }
}
