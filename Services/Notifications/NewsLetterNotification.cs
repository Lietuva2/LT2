using Data.EF.Actions;
using Data.EF.Users;
using Framework.Infrastructure.Logging;
using Framework.Infrastructure.Notification;

namespace Services.Notifications
{
    /// <summary>
    /// Represents new order mailing task.
    /// </summary>
    public class NewsLetterNotification : NotificationTaskServiceBase
    {
        public string News { get; set; }
        public int NewsCount { get; set; }
        public string NewsLetterFreq { get; set; }
        public string NewsCountText { get; set; }

        public NewsLetterNotification(IMailSender mailSender, ILogger logger, IActionsContextFactory context, IUsersContextFactory usersContextFactory)
            : base(mailSender, logger, context, usersContextFactory)
        {
        }

        public bool Execute()
        {
            return Execute(News, NewsCount.ToString(), NewsCountText, NewsLetterFreq);
        }
    }
}
