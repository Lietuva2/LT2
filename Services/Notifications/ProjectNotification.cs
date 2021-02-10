using Data.EF.Actions;
using Data.EF.Users;
using Framework.Infrastructure.Logging;
using Framework.Infrastructure.Notification;

namespace Services.Notifications
{
    /// <summary>
    /// Represents new order mailing task.
    /// </summary>
    public class ProjectNotification : NotificationTaskServiceBase
    {
        public string UserFullName { get; set; }
        public string UserLink { get; set; }
        public string OrganizationName { get; set; }
        public string OrganizationLink { get; set; }
        public string ProjectName { get; set; }
        public string ProjectLink { get; set; }
        public string TodoLink { get; set; }
        public string ToDoSubject { get; set; }
        public string Text { get; set; }

        public ProjectNotification(IMailSender mailSender, ILogger logger, IActionsContextFactory context, IUsersContextFactory usersContextFactory)
            : base(mailSender, logger, context, usersContextFactory)
        {
        }

        public bool Execute()
        {
            return Execute(UserFullName, UserLink, OrganizationName, OrganizationLink, ProjectName, ProjectLink, ToDoSubject, TodoLink, Text);
        }
    }
}
