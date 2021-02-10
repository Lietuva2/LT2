﻿using Data.EF.Actions;
using Data.EF.Users;
using Framework.Infrastructure.Logging;
using Framework.Infrastructure.Notification;

namespace Services.Notifications
{
    /// <summary>
    /// Represents new order mailing task.
    /// </summary>
    public class ObjectCreatedNotification : NotificationTaskServiceBase
    {
        public string ObjectSubject { get; set; }
        public string ObjectLink { get; set; }

        public ObjectCreatedNotification(IMailSender mailSender, ILogger logger, IActionsContextFactory context, IUsersContextFactory usersContextFactory)
            : base(mailSender, logger, context, usersContextFactory)
        {
            IncludeUnsubscribe = true;
        }

        public bool Execute()
        {
            return Execute(ObjectSubject, ObjectLink);
        }
    }
}
