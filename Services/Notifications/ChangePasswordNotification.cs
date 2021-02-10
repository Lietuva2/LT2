using Data.EF.Actions;
using Data.EF.Users;
using Framework.Infrastructure.Logging;
using Framework.Infrastructure.Notification;

namespace Services.Notifications
{
    /// <summary>
    /// Represents new order mailing task.
    /// </summary>
    public class ChangePasswordNotification : NotificationTaskServiceBase
    {
        public string Username { get; set; }
        public string Password { get; set; }

        public ChangePasswordNotification(IMailSender mailSender, ILogger logger, IActionsContextFactory context, IUsersContextFactory usersContextFactory)
            : base(mailSender, logger, context, usersContextFactory)
        {
        }

        public bool Execute()
        {
            return Execute(Username, Password);
        }
    }
}
