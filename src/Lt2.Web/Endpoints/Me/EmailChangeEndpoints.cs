using System.Security.Claims;
using Lt2.Api.Contracts;
using Lt2.Api.Contracts.Me;
using Lt2.Identity;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Me;

/// <summary>
/// Changing the account's email address (design 6.2), on "Saugumas ir duomenys" (<c>#email-address</c>):
/// <list type="bullet">
/// <item><c>POST /api/v1/me/email/proof-code</c>: a code to the address in use, the proof a session not signed in with a passkey
/// gives (202; limited per client address as every code request, and by the sign-in code's limits);</item>
/// <item><c>POST /api/v1/me/email/change</c> with the new address and that code (none in a passkey session): a code goes to the new
/// address, the same 202 whether it is free or not;</item>
/// <item><c>POST /api/v1/me/email/confirm</c> with the code from the new address: the account moves to it, the session goes on
/// under a new key (200, the address);</item>
/// <item><c>POST /api/v1/me/email/restore</c> with the old address's link code: the old address back, every session ended (204).
/// It needs no session and is on <c>TermsExemptions.Routes</c>; like every write of the group it passes the same-origin and
/// antiforgery filter, since only the site's own page sends it (a mail program never does), and like the other one-time links it
/// is limited per client address and answers one refusal for every link that does not work.</item>
/// </list>
/// Codes and the link's code travel only in request bodies.
/// </summary>
internal static class EmailChangeEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapPost(ApiEndpoints.Relative(ApiRoutes.Me.EmailProofCode), ProofCodeAsync)
            .WithName("SendEmailProofCode").WithTags("Me").RequireAuthorization(Policies.Member)
            .RequireRateLimiting(RateLimitPolicies.CodeRequest)
            .Produces(StatusCodes.Status202Accepted);

        api.MapPost(ApiEndpoints.Relative(ApiRoutes.Me.EmailChange), ChangeAsync)
            .WithName("ChangeEmail").WithTags("Me").RequireAuthorization(Policies.Member)
            .WithValidation<EmailChangeRequest>()
            .RequireRateLimiting(RateLimitPolicies.CodeRequest)
            .Produces(StatusCodes.Status202Accepted);

        api.MapPost(ApiEndpoints.Relative(ApiRoutes.Me.EmailConfirm), ConfirmAsync)
            .WithName("ConfirmEmailChange").WithTags("Me").RequireAuthorization(Policies.Member)
            .WithValidation<EmailConfirmRequest>()
            .RequireRateLimiting(RateLimitPolicies.SignIn)
            .Produces<EmailChangedResponse>();

        api.MapPost(ApiEndpoints.Relative(ApiRoutes.Me.EmailRestore), RestoreAsync)
            .WithName("RestoreEmail").WithTags("Me").AllowAnonymous()
            .WithValidation<EmailRestoreRequest>()
            .RequireRateLimiting(RateLimitPolicies.OneTimeCode)
            .Produces(StatusCodes.Status204NoContent);
    }

    private static async Task<IResult> ProofCodeAsync(ClaimsPrincipal user, IIdentityModule identity, ErrorMapper errors, CancellationToken ct)
    {
        var result = await identity.SendEmailProofCodeAsync(user, ct);
        return result.IsSuccess ? TypedResults.StatusCode(StatusCodes.Status202Accepted) : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> ChangeAsync(EmailChangeRequest request, ClaimsPrincipal user, IIdentityModule identity, ErrorMapper errors, CancellationToken ct)
    {
        var result = await identity.ChangeEmailAsync(user, request.Email, request.ProofCode, ct);
        return result.IsSuccess ? TypedResults.StatusCode(StatusCodes.Status202Accepted) : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> ConfirmAsync(
        EmailConfirmRequest request, ClaimsPrincipal user, HttpContext context, IpHasher ipHasher, IIdentityModule identity, ErrorMapper errors, CancellationToken ct)
    {
        var result = await identity.ConfirmEmailChangeAsync(user, request.Code, ipHasher.Hash(context), ct);
        return result.IsSuccess ? TypedResults.Ok(new EmailChangedResponse(result.Value!)) : errors.ToResult(result.Error!);
    }

    private static async Task<IResult> RestoreAsync(
        EmailRestoreRequest request, HttpContext context, IpHasher ipHasher, IIdentityModule identity, ErrorMapper errors, CancellationToken ct)
    {
        var result = await identity.RestoreEmailAsync(request.Token, ipHasher.Hash(context), ct);
        context.Response.Headers.CacheControl = "no-store";
        return result.IsSuccess ? TypedResults.NoContent() : errors.ToResult(result.Error!);
    }
}
