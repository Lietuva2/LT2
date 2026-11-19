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
        services.AddScoped<IValidator<Me.MyBillsQuery>, Me.MyBillsQueryValidator>();
        services.AddScoped<IValidator<Me.RoleActivationRequest>, Me.RoleActivationRequestValidator>();
        services.AddScoped<IValidator<GrantRoleRequest>, GrantRoleRequestValidator>();
        services.AddScoped<IValidator<ResetAccountRequest>, ResetAccountRequestValidator>();
        services.AddScoped<IValidator<UpdateSettingRequest>, UpdateSettingRequestValidator>();
        services.AddScoped<IValidator<Me.PositionConsentRequest>, Me.PositionConsentRequestValidator>();
        services.AddScoped<IValidator<Me.TermsAcceptanceRequest>, Me.TermsAcceptanceRequestValidator>();
        services.AddScoped<IValidator<Me.EmailChangeRequest>, Me.EmailChangeRequestValidator>();
        services.AddScoped<IValidator<Me.EmailConfirmRequest>, Me.EmailConfirmRequestValidator>();
        services.AddScoped<IValidator<Me.EmailRestoreRequest>, Me.EmailRestoreRequestValidator>();
        services.AddScoped<IValidator<Me.ProfileRequest>, Me.ProfileRequestValidator>();
        services.AddScoped<IValidator<Me.MemberReportRequest>, Me.MemberReportRequestValidator>();
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
        services.AddScoped<IValidator<Appeals.CloseRecordRequest>, Appeals.CloseRecordRequestValidator>();
        services.AddScoped<IValidator<Appeals.MergeReasonRequest>, Appeals.MergeReasonRequestValidator>();
        services.AddScoped<IValidator<Appeals.FlagRequest>, Appeals.FlagRequestValidator>();
        services.AddScoped<IValidator<Appeals.ModeratorPlannedRequest>, Appeals.ModeratorPlannedRequestValidator>();
        services.AddScoped<IValidator<Digests.UrgentDigestRequest>, Appeals.UrgentDigestRequestValidator>();
        services.AddScoped<IValidator<Appeals.DescriptionDraftRequest>, Appeals.DescriptionDraftRequestValidator>();
        services.AddScoped<IValidator<Organisations.OrganisationRequest>, Organisations.OrganisationRequestValidator>();
        services.AddScoped<IValidator<Organisations.OrganisationStatementRequest>, Organisations.OrganisationStatementRequestValidator>();
        services.AddScoped<IValidator<Organisations.ConfirmationCodeRequest>, Organisations.ConfirmationCodeRequestValidator>();
        services.AddScoped<IValidator<Organisations.ConfirmationRequest>, Organisations.ConfirmationRequestValidator>();
        services.AddScoped<IValidator<Mps.MpTakeoverRequest>, Mps.MpTakeoverRequestValidator>();
        services.AddScoped<IValidator<Mps.MpOfficeAcceptanceRequest>, Mps.MpOfficeAcceptanceRequestValidator>();
        services.AddScoped<IValidator<Mps.MpOfficeInvitationRequest>, Mps.MpOfficeInvitationRequestValidator>();
        services.AddScoped<IValidator<Mps.MpResponseRequest>, Mps.MpResponseRequestValidator>();
        services.AddScoped<IValidator<Comments.CommentRequest>, Comments.CommentRequestValidator>();
        services.AddScoped<IValidator<Digests.CancelDigestRequest>, Digests.CancelDigestRequestValidator>();
        services.AddScoped<IValidator<Digests.MpMailFrequencyRequest>, Digests.MpMailFrequencyRequestValidator>();
        services.AddScoped<IValidator<Digests.MpMailExtraRequest>, Digests.MpMailExtraRequestValidator>();
        services.AddScoped<IValidator<Digests.MpMailTokenRequest>, Digests.MpMailTokenRequestValidator>();
        services.AddScoped<IValidator<Digests.IntroductionRequest>, Digests.IntroductionRequestValidator>();
        services.AddScoped<IValidator<Digests.SuppressRequest>, Digests.SuppressRequestValidator>();
        services.AddScoped<IValidator<Digests.UnsuppressRequest>, Digests.UnsuppressRequestValidator>();
        return services;
    }
}
