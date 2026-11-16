using System.Resources;
using Lt2.Notifications.Infrastructure;
using Lt2.Notifications.Outbox;
using Lt2.Notifications.Templates;
using Lt2.Notifications.Transports;
using Lt2.SharedKernel;
using Lt2.SharedKernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

[assembly: NeutralResourcesLanguage("lt")]

namespace Lt2.Notifications;

/// <summary>Registration of the Notifications module: outbox, dispatcher, email transport and templates.</summary>
public static class NotificationsModule
{
    public const string Schema = "notify";

    /// <summary>Registers the outbox writer every host needs.</summary>
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // A factory as well as the scoped context: pages read summaries while other components render.
        services.AddDbContextFactory<NotificationsDb>((sp, options) =>
            options.UseLt2Npgsql(sp.GetRequiredService<NpgsqlDataSource>(), Schema), ServiceLifetime.Scoped);
        services.AddSingleton<IOutbox, OutboxWriter>();
        services.AddLocalization();

        // The site's own address for links in emails (design 5.6); empty keeps the paths.
        services.AddOptions<SiteOptions>().Bind(configuration.GetSection(SiteOptions.Section))
            .Validate(o => SiteLinks.IsValid(o.Address), "Site:Address must be empty or an https address of a host and nothing more, for example https://lietuva2.lt.")
            .ValidateOnStart();
        // Design 8.19: real sending, the MPs' choices, the introduction letter and the suppression list. The preview and the
        // email share one template; the keys and the deployment's locks are configuration (Security:MailHashKey, Demo:Allowed,
        // Email:Caught).
        services.AddSingleton<MpMailTemplates>();
        services.AddSingleton(new MpMail.MailKeys(configuration));
        services.AddScoped(sp => new MpMail.MailLocks(configuration, sp.GetRequiredService<IMpMailAddresses>()));
        services.AddScoped<MpMail.MpSender>();
        services.AddScoped<MpMail.MpMailService>();
        services.AddScoped<IMpMail>(sp => sp.GetRequiredService<MpMail.MpMailService>());
        services.AddScoped<IMpMailAdministration>(sp => sp.GetRequiredService<MpMail.MpMailService>());
        services.AddScoped<IDigests, Digests.DigestService>();
        services.AddScoped<IFollows, Follows.FollowService>();
        services.AddScoped<IAccountData, Follows.FollowAccountData>();
        services.AddSingleton<IModuleDatabase, NotificationsModuleDatabase>();
        return services;
    }

    /// <summary>Registers what only the worker needs: the dispatcher, the transport and the templates.</summary>
    public static IServiceCollection AddNotificationsDispatcher(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddLocalization();
        services.AddOptions<SmtpOptions>().Bind(configuration.GetSection(SmtpOptions.Section)).ValidateOnStart();
        services.AddOptions<OutboxOptions>().Bind(configuration.GetSection(OutboxOptions.Section));
        services.AddOptions<MpStreamOptions>().Bind(configuration.GetSection(MpStreamOptions.Section));
        services.AddSingleton<IEmailTransport, SmtpEmailTransport>();
        services.AddSingleton<SignInCodeTemplate>();
        services.AddSingleton<AccountNoticeTemplate>();
        services.AddSingleton<ModerationNoticeTemplate>();
        services.AddSingleton<WritingRestrictionTemplate>();
        services.AddSingleton<FeedbackTemplates>();
        services.AddSingleton<OrganisationTemplates>();
        services.AddSingleton<FollowTemplates>();
        services.AddSingleton<MpTemplates>();

        // Every outbox kind and the template that renders it.
        services.AddEmail<SignInCodeEmail, SignInCodeTemplate>(MessageKinds.SignInCodeEmail, t => t.Render);
        services.AddEmail<AccountNoticeEmail, AccountNoticeTemplate>(MessageKinds.AccountNoticeEmail, t => t.Render);
        services.AddEmail<ModerationNoticeEmail, ModerationNoticeTemplate>(MessageKinds.ModerationNoticeEmail, t => t.Render);
        services.AddEmail<WritingRestrictionEmail, WritingRestrictionTemplate>(MessageKinds.WritingRestrictionEmail, t => t.Render);
        services.AddEmail<FeedbackReplyEmail, FeedbackTemplates>(MessageKinds.FeedbackReplyEmail, t => t.Reply);
        services.AddEmail<FeedbackNoticeEmail, FeedbackTemplates>(MessageKinds.FeedbackNoticeEmail, t => t.Notice);
        services.AddEmail<OrganisationCodeEmail, OrganisationTemplates>(MessageKinds.OrganisationCodeEmail, t => t.Render);
        services.AddEmail<OrganisationNoticeEmail, OrganisationTemplates>(MessageKinds.OrganisationNoticeEmail, t => t.Render);
        services.AddEmail<FollowPlannedEmail, FollowTemplates>(MessageKinds.FollowPlannedEmail, t => t.Planned);
        services.AddEmail<FollowVotedEmail, FollowTemplates>(MessageKinds.FollowVotedEmail, t => t.Voted);
        services.AddEmail<FollowStatementEmail, FollowTemplates>(MessageKinds.FollowStatementEmail, t => t.Statement);
        services.AddEmail<FollowResponsesEmail, FollowTemplates>(MessageKinds.FollowResponsesEmail, t => t.Responses);
        services.AddEmail<CommentNoticeEmail, FollowTemplates>(MessageKinds.CommentNoticeEmail, t => t.Comments);
        services.AddEmail<MpTakeoverLinkEmail, MpTemplates>(MessageKinds.MpTakeoverLinkEmail, t => t.Render);
        services.AddEmail<MpResponseNoticeEmail, MpTemplates>(MessageKinds.MpResponseNoticeEmail, t => t.Response);
        services.AddEmail<MpMail.MpDigestEmail, MpMailTemplates>(MpMail.MpMailKinds.Digest, t => t.Digest);
        services.AddEmail<MpMail.MpIntroductionEmail, MpMailTemplates>(MpMail.MpMailKinds.Introduction, t => t.Introduction);
        services.AddEmail<MpMail.MpMailNoticeEmail, MpMailTemplates>(MpMail.MpMailKinds.Notice, t => t.Notice);
        services.AddSingleton<INotificationsDispatcher, OutboxDispatcher>();
        return services;
    }

    private static void AddEmail<TModel, TTemplate>(this IServiceCollection services, string kind, Func<TTemplate, Func<TModel, EmailMessage>> render)
        where TModel : class
        where TTemplate : notnull =>
        services.AddSingleton<IEmailRenderer>(sp => new EmailRenderer<TModel>(kind, render(sp.GetRequiredService<TTemplate>())));
}