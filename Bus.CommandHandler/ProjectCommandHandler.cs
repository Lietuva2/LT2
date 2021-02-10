using Bus.Commands;
using Framework.Infrastructure.Logging;

using Services.ModelServices;
using Services.Notifications;

namespace Bus.CommandHandler
{
    public class ProjectCommandHandler : ActionCommandHandler<ProjectCommand>
    {
        public readonly NotificationService notificationService;
        public readonly ProjectNotification notification;

        public ProjectCommandHandler(NotificationService notificationService, ProjectNotification notification)
        {
            this.notificationService = notificationService;
            this.notification = notification;
        }

        /// <summary>
        /// Handles completed virtual machine command.
        /// </summary>
        /// <param name="command">Completed virtual machine command.</param>
        protected override void ProcessCommand(ProjectCommand command)
        {
            Service.ProcessProjectCommand(command, command.MileStoneId, command.ToDoId, command.ToDoLink, command.CommentId);

            if (command.SendNotifications)
            {
                notificationService.SendProjectNotification(notification, command);
            }
        }
    }
}
