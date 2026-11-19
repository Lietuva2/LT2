using FluentValidation;
using Lt2.Api.Contracts;
using Lt2.Web.Endpoints.Admin;
using Lt2.Web.Endpoints.Appeals;
using Lt2.Web.Endpoints.Auth;
using Lt2.Web.Endpoints.Digests;
using Lt2.Web.Endpoints.Me;
using Lt2.Web.Endpoints.Moderation;
using Lt2.Web.Endpoints.Parliament;
using Lt2.Web.Security;
using Lt2.Web.Services;

namespace Lt2.Web.Endpoints;

/// <summary>Maps every <c>/api/v1</c> endpoint. Each endpoint lives in its own file, grouped by module.</summary>
internal static class ApiEndpoints
{
    public static void MapLt2Api(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup(ApiRoutes.Prefix)
            .AddEndpointFilter<AntiforgeryAndOriginFilter>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        AntiforgeryTokenEndpoint.Map(api);
        RegisterEndpoint.Map(api);
        BotChallengeEndpoint.Map(api);
        RequestCodeEndpoint.Map(api);
        SignInEndpoint.Map(api);
        SignOutEndpoint.Map(api);
        PasskeyCreationOptionsEndpoint.Map(api);
        AddPasskeyEndpoint.Map(api);
        PasskeyRequestOptionsEndpoint.Map(api);
        PasskeySignInEndpoint.Map(api);

        GetMeEndpoint.Map(api);
        CompleteRegistrationEndpoint.Map(api);
        ActivateRoleEndpoint.Map(api);
        ConfirmPasskeyEndpoint.Map(api);
        StartStaffSessionEndpoint.Map(api);
        StartPasskeySessionEndpoint.Map(api);
        GetSessionsEndpoint.Map(api);
        EndSessionEndpoint.Map(api);
        KnownBrowserEndpoints.Map(api);
        EmailChangeEndpoints.Map(api);
        GivePositionConsentEndpoint.Map(api);
        AcceptTermsEndpoint.Map(api);
        WithdrawPositionConsentEndpoint.Map(api);
        PartyWithdrawalEndpoints.Map(api);
        ExportEndpoint.Map(api);
        DeleteAccountEndpoint.Map(api);

        GetSettingsEndpoint.Map(api);
        UpdateSettingEndpoint.Map(api);
        GrantRoleEndpoint.Map(api);
        ResetAccountEndpoint.Map(api);
        AccountLockEndpoints.Map(api);

        GetCurrentTermEndpoint.Map(api);
        GetMpsEndpoint.Map(api);
        GetMpEndpoint.Map(api);
        GetMpVotesEndpoint.Map(api);
        GetFactionsEndpoint.Map(api);
        GetFactionEndpoint.Map(api);
        GetCommitteesEndpoint.Map(api);
        GetCommitteeEndpoint.Map(api);
        GetBillsEndpoint.Map(api);
        GetBillEndpoint.Map(api);
        GetVotesEndpoint.Map(api);
        GetVoteEndpoint.Map(api);
        GetImportsEndpoint.Map(api);

        GetAppealRulesEndpoint.Map(api);
        GetAppealsEndpoint.Map(api);
        GetRecordEndpoint.Map(api);
        GetReasonsEndpoint.Map(api);
        GetReasonEndpoint.Map(api);
        GetMyRecordEndpoint.Map(api);
        GetRecordHistoryEndpoint.Map(api);
        TakePositionEndpoint.Map(api);
        ClearPositionEndpoint.Map(api);
        FindSimilarReasonsEndpoint.Map(api);
        WriteReasonEndpoint.Map(api);
        EditReasonEndpoint.Map(api);
        EndorseEndpoint.Map(api);
        RemoveEndorsementEndpoint.Map(api);
        WithdrawReasonEndpoint.Map(api);
        GetMyRestrictionEndpoint.Map(api);
        Me.InvitationEndpoints.Map(api);
        Admin.InvitationEndpoints.Map(api);
        Admin.UsageEndpoint.Map(api);
        Feedback.FeedbackEndpoints.Map(api);
        ReportEndpoint.Map(api);
        FlagDateEndpoint.Map(api);

        GetModerationQueueEndpoint.Map(api);
        GetReasonAuthorEndpoint.Map(api);
        RestrictAuthorEndpoints.Map(api);
        RestrictionsEndpoints.Map(api);
        KeepRecordEndpoint.Map(api);
        HideRecordEndpoint.Map(api);
        CloseRecordEndpoint.Map(api);
        ReopenRecordEndpoint.Map(api);
        HiddenEndpoints.Map(api);
        SetRecordThresholdEndpoint.Map(api);
        ModerationRecordEndpoints.Map(api);
        PublishReasonEndpoint.Map(api);
        HideReasonEndpoint.Map(api);
        MergeReasonEndpoint.Map(api);
        SetPlannedEndpoint.Map(api);
        HoldRecordEndpoint.Map(api);
        ReleaseRecordEndpoint.Map(api);
        UrgentDigestEndpoint.Map(api);

        GetDigestRulesEndpoint.Map(api);
        GetDigestsEndpoint.Map(api);
        GetDigestEndpoint.Map(api);
        GetRecordDigestsEndpoint.Map(api);
        GetRecordNextDigestEndpoint.Map(api);
        DigestSendingEndpoints.Map(api);
        MpMail.MpMailEndpoints.Map(api);
        MpSendingAdminEndpoints.Map(api);

        // Design 8.19.4: the one-click stop is the one write a mail program sends, without an Origin header or an antiforgery
        // token, so it alone is mapped on the application, outside the group's filter (the group adds the filter to every
        // other route, the other token routes included).
        MpMail.OneClickStopEndpoint.Map(app);

        // Design 8.24.1: a member's stop link, for the same reason the second such write.
        Me.NoticeStopEndpoint.Map(app);

        Agenda.GetAgendaEndpoint.Map(api);
        FollowEndpoints.Map(api);
        Members.MemberEndpoints.Map(api);
        NoticeFrequencyEndpoint.Map(api);
        MyBillsEndpoint.Map(api);
        Analytics.GetAnalyticsEndpoint.Map(api);
        Analytics.GetMyAgreementEndpoint.Map(api);

        Organisations.OrganisationEndpoints.Map(api);
        Organisations.OrganisationRepresentativeEndpoints.Map(api);
        Organisations.StatementEndpoints.Map(api);
        Organisations.OrganisationModerationEndpoints.Map(api);

        Mps.ClaimProfileEndpoint.Map(api);
        Mps.TakeOverProfileEndpoint.Map(api);
        Mps.MyMpEndpoints.Map(api);
        Mps.MpResponseEndpoints.Map(api);
        Comments.CommentEndpoints.Map(api);
        EndMpLinkEndpoint.Map(api);
    }

    /// <summary>A route from <see cref="ApiRoutes"/> relative to the <c>/api/v1</c> group.</summary>
    public static string Relative(string route) => route[ApiRoutes.Prefix.Length..];

    /// <summary>Runs the shared FluentValidation validator for <typeparamref name="T"/> before the handler.</summary>
    public static RouteHandlerBuilder WithValidation<T>(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<ValidationFilter<T>>().ProducesValidationProblem();
}

internal sealed class ValidationFilter<T>(IValidator<T> validator, ErrorMapper errors) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var argument = context.Arguments.OfType<T>().FirstOrDefault();
        if (argument is null)
        {
            return errors.ValidationFailed(new Dictionary<string, string[]> { ["body"] = ["?"] });
        }

        var result = await validator.ValidateAsync(argument, context.HttpContext.RequestAborted);
        if (result.IsValid)
        {
            return await next(context);
        }

        return errors.ValidationFailed(result.Errors
            .GroupBy(e => JsonName(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));
    }

    private static string JsonName(string property) =>
        string.IsNullOrEmpty(property) ? property : char.ToLowerInvariant(property[0]) + property[1..];
}
