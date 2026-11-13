using FluentValidation;
using Lt2.Api.Contracts.Admin;
using Lt2.Api.Contracts.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace Lt2.Api.Contracts;

public static class ContractsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared validators explicitly (no assembly scanning, so trimming in the browser keeps them).
    /// The same validators run in the browser and on the server (design 3).
    /// </summary>
    public static IServiceCollection AddLt2Validators(this IServiceCollection services)
    {
        services.AddScoped<IValidator<RegisterRequest>, RegisterRequestValidator>();
        services.AddScoped<IValidator<RequestCodeRequest>, RequestCodeRequestValidator>();
        services.AddScoped<IValidator<SignInRequest>, SignInRequestValidator>();
        services.AddScoped<IValidator<PasskeyCredentialRequest>, PasskeyCredentialRequestValidator>();
        services.AddScoped<IValidator<Me.CompleteRegistrationRequest>, Me.CompleteRegistrationRequestValidator>();
        services.AddScoped<IValidator<Me.RoleActivationRequest>, Me.RoleActivationRequestValidator>();
        services.AddScoped<IValidator<GrantRoleRequest>, GrantRoleRequestValidator>();
        services.AddScoped<IValidator<ResetAccountRequest>, ResetAccountRequestValidator>();
        services.AddScoped<IValidator<UpdateSettingRequest>, UpdateSettingRequestValidator>();
        services.AddScoped<IValidator<Me.PositionConsentRequest>, Me.PositionConsentRequestValidator>();
        services.AddScoped<IValidator<Me.PartySupportRequest>, Me.PartySupportRequestValidator>();
        services.AddScoped<IValidator<Appeals.OpenAppealRequest>, Appeals.OpenAppealRequestValidator>();
        services.AddScoped<IValidator<Appeals.EditAppealRequest>, Appeals.EditAppealRequestValidator>();
        services.AddScoped<IValidator<Appeals.ThresholdRequest>, Appeals.ThresholdRequestValidator>();
        services.AddScoped<IValidator<Appeals.PositionRequest>, Appeals.PositionRequestValidator>();
        services.AddScoped<IValidator<Appeals.ReasonRequest>, Appeals.ReasonRequestValidator>();
        services.AddScoped<IValidator<Appeals.ReportRequest>, Appeals.ReportRequestValidator>();
        services.AddScoped<IValidator<Appeals.ModerationRequest>, Appeals.ModerationRequestValidator>();
        services.AddScoped<IValidator<Appeals.StatementRequest>, Appeals.StatementRequestValidator>();
        services.AddScoped<IValidator<Appeals.RestrictionRequest>, Appeals.RestrictionRequestValidator>();
        services.AddScoped<IValidator<Feedback.FeedbackRequest>, Feedback.FeedbackRequestValidator>();
        services.AddScoped<IValidator<Feedback.FeedbackReplyRequest>, Feedback.FeedbackReplyRequestValidator>();
        services.AddScoped<IValidator<Appeals.ModeratorThresholdRequest>, Appeals.ModeratorThresholdRequestValidator>();
        services.AddScoped<IValidator<Appeals.ModeratorDescriptionRequest>, Appeals.ModeratorDescriptionRequestValidator>();
        services.AddScoped<IValidator<Appeals.MergeReasonRequest>, Appeals.MergeReasonRequestValidator>();
        services.AddScoped<IValidator<Appeals.FlagRequest>, Appeals.FlagRequestValidator>();
        services.AddScoped<IValidator<Appeals.ModeratorPlannedRequest>, Appeals.ModeratorPlannedRequestValidator>();
        services.AddScoped<IValidator<Digests.UrgentDigestRequest>, Appeals.UrgentDigestRequestValidator>();
        return services;
    }
}
