using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Admin;
using Lt2.Api.Contracts.Appeals;
using Lt2.Appeals;
using Lt2.Identity;
using Lt2.SharedKernel;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Admin;

/// <summary>
/// The full lock of an account (design 8.3), administrators only, in a staff-passkey session with a fresh passkey confirmation,
/// each with a statement that is audited and emailed to the person. By address (<c>POST /api/v1/admin/accounts/lock</c> and
/// <c>.../unlock</c>, the secondary path: the owner wrote from their address), from an item its person wrote
/// (<c>POST .../lock-author</c>: the server finds the account, the address is never sent), and from the list of locked accounts
/// (<c>GET .../locked</c>: id, name, day, statement, never an address; <c>POST .../{id}/unlock</c>). Never a moderator.
/// </summary>
internal static class AccountLockEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        foreach (var (route, locked) in new[] { (ApiRoutes.Admin.LockAccount, true), (ApiRoutes.Admin.UnlockAccount, false) })
        {
            api.MapPost(ApiEndpoints.Relative(route), (AccountLockRequest request, ClaimsPrincipal user, HttpContext context, IIdentityModule identity,
                    IIdentityAdministration administration, IpHasher ipHasher, ErrorMapper errors, IConfiguration configuration, CancellationToken cancellationToken) =>
                    HandleAsync(locked, request, user, context, identity, administration, ipHasher, errors, configuration, cancellationToken))
                .WithName(locked ? "LockAccount" : "UnlockAccount")
                .WithTags("Admin")
                .RequireAuthorization(Policies.Administrator)
                .WithValidation<AccountLockRequest>()
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict);
        }

        api.MapPost(ApiEndpoints.Relative(ApiRoutes.Admin.LockAuthor), (AuthorLockRequest request, ClaimsPrincipal user, HttpContext context, IIdentityModule identity,
                IIdentityAdministration administration, IAuthorLookup authors, IpHasher ipHasher, ErrorMapper errors, IConfiguration configuration, CancellationToken cancellationToken) =>
                ActAsync(user, context, identity, ipHasher, errors, administrator =>
                    AuthorLock.LockAsync(request, administrator, AppealMapping.Contact(configuration), authors, administration, cancellationToken), cancellationToken))
            .WithName("LockAuthor")
            .WithTags("Admin")
            .RequireAuthorization(Policies.Administrator)
            .WithValidation<AuthorLockRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapGet(ApiEndpoints.Relative(ApiRoutes.Admin.LockedAccounts), async (IIdentityAdministration administration, CancellationToken cancellationToken) =>
                TypedResults.Ok(await AuthorLock.ListAsync(administration, cancellationToken)))
            .WithName("GetLockedAccounts")
            .WithTags("Admin")
            .RequireAuthorization(Policies.Administrator)
            .Produces<IReadOnlyList<LockedAccountResponse>>();

        api.MapPost(ApiEndpoints.Relative(ApiRoutes.Admin.UnlockById), (Guid id, StatementRequest request, ClaimsPrincipal user, HttpContext context, IIdentityModule identity,
                IIdentityAdministration administration, IpHasher ipHasher, ErrorMapper errors, IConfiguration configuration, CancellationToken cancellationToken) =>
                ActAsync(user, context, identity, ipHasher, errors, administrator =>
                    administration.UnlockAccountByIdAsync(id, request.Statement, administrator, AppealMapping.Contact(configuration), cancellationToken), cancellationToken))
            .WithName("UnlockAccountById")
            .WithTags("Admin")
            .RequireAuthorization(Policies.Administrator)
            .WithValidation<StatementRequest>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> HandleAsync(
        bool locked,
        AccountLockRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        IIdentityModule identity,
        IIdentityAdministration administration,
        IpHasher ipHasher,
        ErrorMapper errors,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var contact = AppealMapping.Contact(configuration);
        return await ActAsync(user, context, identity, ipHasher, errors, administrator => locked
            ? administration.LockAccountAsync(request.Email, request.Statement, administrator, contact, cancellationToken)
            : administration.UnlockAccountAsync(request.Email, request.Statement, administrator, contact, cancellationToken), cancellationToken);
    }

    /// <summary>The fresh passkey confirmation first, then the action as this administrator.</summary>
    private static async Task<IResult> ActAsync(
        ClaimsPrincipal user,
        HttpContext context,
        IIdentityModule identity,
        IpHasher ipHasher,
        ErrorMapper errors,
        Func<StaffActor, Task<Result<Unit>>> action,
        CancellationToken cancellationToken)
    {
        if (await FreshConfirmation.RequireAsync(identity, user, errors, cancellationToken) is { } refused)
        {
            return refused;
        }

        var result = await action(new StaffActor(user.UserId()!.Value, Roles.Administrator, ipHasher.Hash(context)));
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}

/// <summary>
/// The lock reached from an item, and the list of locked accounts (design 8.3), as the endpoints and the in-process client both do
/// it: the item's author is found by the module that owns the item and only the account's id goes to Identity.
/// </summary>
internal static class AuthorLock
{
    public static async Task<Result<Unit>> LockAsync(
        AuthorLockRequest request, StaffActor administrator, string? contact, IAuthorLookup authors, IIdentityAdministration administration, CancellationToken cancellationToken)
    {
        // A reported profile is named by the moderation id the moderators' list already carries; the other items by their own id.
        var userId = request.TargetId;
        if (request.TargetKind != LockTargets.Profile)
        {
            var found = await authors.AuthorOfAsync(request.TargetKind, request.TargetId, cancellationToken);
            if (!found.IsSuccess)
            {
                return found.Error!;
            }

            userId = found.Value;
        }

        return await administration.LockAccountByIdAsync(userId, request.Statement, administrator, contact, cancellationToken);
    }

    public static async Task<IReadOnlyList<LockedAccountResponse>> ListAsync(IIdentityAdministration administration, CancellationToken cancellationToken) =>
        (await administration.LockedAccountsAsync(cancellationToken)).Select(a => new LockedAccountResponse(a.UserId, a.DisplayName, a.LockedAt, a.Statement)).ToList();
}