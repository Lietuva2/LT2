using Lt2.Api.Contracts.Mps;
using Lt2.Identity;
using Lt2.SharedKernel;
using Lt2.Web.Endpoints.Auth;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints.Mps;

/// <summary>
/// <c>POST /api/v1/mps/registration-sign-in</c> (design 8.5.2): the take-over link sent with a registration to an MP's official
/// address (its token from the page's <c>#</c>, in the body) signs the pending account in; the take-over page then completes the
/// registration and takes the profile over with the same token. One answer for every link that does not work.
/// </summary>
internal static class RegistrationSignInEndpoint
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost(ApiEndpoints.Relative(MpRoutes.RegistrationSignIn), HandleAsync)
            .WithName("SignInWithRegistrationLink")
            .WithTags("Mps")
            .WithValidation<MpRegistrationSignInRequest>()
            .RequireRateLimiting(RateLimitPolicies.OneTimeCode)
            .Produces<Lt2.Api.Contracts.Auth.SignInResponse>();

    private static async Task<IResult> HandleAsync(
        MpRegistrationSignInRequest request,
        HttpContext context,
        IPoliticians politicians,
        AnalyticsCopies analytics,
        ErrorMapper errors,
        CancellationToken cancellationToken)
    {
        var before = context.User.UserId();
        var result = await politicians.SignInWithRegistrationLinkAsync(request.Token, cancellationToken);
        return result.IsSuccess ? SignInEndpoint.Answer(result.Value, before, analytics) : errors.ToResult(result.Error!);
    }
}
