using System;
using Data.EF.Actions;
using Data.EF.Users;
using Framework.Infrastructure.Logging;
using Framework.Infrastructure.Notification;

namespace Services.Notifications
{
    /// <summary>
    /// Represents new order mailing task.
    /// </summary>
    public class UserNotConfirmedNotification : NotificationTaskServiceBase
    {
        public UserNotConfirmedNotification(IMailSender mailSender, ILogger logger, IActionsContextFactory context, IUsersContextFactory usersContextFactory)
            : base(mailSender,logger, context, usersContextFactory)
        {
            IncludeUnsubscribe = false;
        }

        public bool Execute()
        {
            return base.Execute();
        }
    }
}
