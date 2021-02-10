using Data.EF.Actions;
using Data.EF.Users;
using Framework.Infrastructure.Logging;
using Framework.Infrastructure.Notification;

namespace Services.Notifications
{
    /// <summary>
    /// Represents new order mailing task.
    /// </summary>
    public class InviteNotification : NotificationTaskServiceBase
    {
        public string ObjectName { get; set; }
        public string ObjectLink { get; set; }
        public string UserFullName { get; set; }
        public string UserLink { get; set; }
        public string UserRole { get; set; }
        public string CustomText { get; set; }

        public InviteNotification(IMailSender mailSender, ILogger logger, IActionsContextFactory context, IUsersContextFactory usersContextFactory)
            : base(mailSender, logger, context, usersContextFactory)
        {
        }

        public bool Execute()
        {
            return Execute(ObjectName, ObjectLink, UserFullName, UserLink, UserRole, CustomText);
        }
    }
}
