using System.Resources;
using Lt2.Notifications.Infrastructure;
using Lt2.Notifications.Outbox;
using Lt2.Notifications.Templates;
using Lt2.Notifications.Transports;
using Lt2.SharedKernel;
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
        services.AddSingleton<Digests.DigestText>();
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
        services.AddSingleton<IEmailTransport, SmtpEmailTransport>();
        services.AddSingleton<SignInCodeTemplate>();
        services.AddSingleton<IOutboxHandler, SignInCodeEmailHandler>();
        services.AddSingleton<AccountNoticeTemplate>();
        services.AddSingleton<IOutboxHandler, AccountNoticeEmailHandler>();
        services.AddSingleton<ModerationNoticeTemplate>();
        services.AddSingleton<IOutboxHandler, ModerationNoticeEmailHandler>();
        services.AddSingleton<WritingRestrictionTemplate>();
        services.AddSingleton<IOutboxHandler, WritingRestrictionEmailHandler>();
        services.AddSingleton<FeedbackTemplates>();
        services.AddSingleton<IOutboxHandler, FeedbackReplyEmailHandler>();
        services.AddSingleton<IOutboxHandler, FeedbackNoticeEmailHandler>();
        services.AddSingleton<FollowTemplates>();
        services.AddSingleton<IOutboxHandler, FollowPlannedEmailHandler>();
        services.AddSingleton<IOutboxHandler, FollowVotedEmailHandler>();
        services.AddSingleton<INotificationsDispatcher, OutboxDispatcher>();
        return services;
    }
}
