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

/// <summary>The kinds of item an administrator locks the author of (design 8.3): the same places the writing restriction is reached from, and a public profile.</summary>
public static class LockTargets
{
    public const string Reason = "reason";
    public const string Comment = "comment";
    public const string Statement = "statement";
    public const string Profile = "profile";

    public static readonly IReadOnlyList<string> All = [Reason, Comment, Statement, Profile];
}

/// <summary>
/// Locks the account of the person who wrote an item (design 8.3): <see cref="TargetKind"/> and <see cref="TargetId"/> name the
/// item (an argument, a comment, an organisation's statement, or a reported profile by its moderation id); the server finds the
/// person, whose address nobody sees. The statement is required, audited and emailed to the person.
/// </summary>
public sealed record AuthorLockRequest
{
    public string TargetKind { get; init; } = "";

    public Guid TargetId { get; init; }

    public string Statement { get; init; } = "";
}

public sealed class AuthorLockRequestValidator : AbstractValidator<AuthorLockRequest>
{
    public AuthorLockRequestValidator(IStringLocalizer<ValidationMessages> l)
    {
        RuleFor(x => x.TargetKind).Must(k => LockTargets.All.Contains(k)).WithMessage(l["Lock.Target"]);
        RuleFor(x => x.TargetId).NotEmpty().WithMessage(l["Lock.Target"]);
        RuleFor(x => (string?)x.Statement).Statement(l, required: true).OverridePropertyName("statement");
    }
}

/// <summary>
/// A locked account for the administrators' list (design 8.3): its id (for unlocking), its display name (null: none), when it was
/// locked and the statement given. Never the address.
/// </summary>
public sealed record LockedAccountResponse(Guid Id, string? DisplayName, DateTimeOffset LockedAt, string? Statement);

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
