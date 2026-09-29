using Data.EF.Actions;
using Data.EF.Users;
using Framework.Infrastructure.Logging;
using Framework.Infrastructure.Notification;

namespace Services.Notifications
{
    /// <summary>
    /// Represents new order mailing task.
    /// </summary>
    public class ConfirmNotification : NotificationTaskServiceBase
    {
        public string ConfirmationCode { get; set; }
        public string Email { get; set; }

        public ConfirmNotification(IMailSender mailSender, ILogger logger, IActionsContextFactory context, IUsersContextFactory usersContextFactory)
            : base(mailSender, logger, context, usersContextFactory)
        {
        }

        public bool Execute()
        {
            return Execute(ConfirmationCode, Email);
        }
    }
}
