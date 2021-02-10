using System;
using System.Linq;
using System.Text;
using Bus.Commands;
using Data.Enums;
using Data.Infrastructure.Sessions;
using Data.MongoDB;
using Framework.Infrastructure.Logging;
using Framework.Other;
using MongoDB.Bson;

using Services.ModelServices;
using Services.Notifications;

namespace Bus.CommandHandler
{
    public class IssueCommandHandler : ActionCommandHandler<IssueCommand>
    {
        public readonly NotificationService notificationService;
        public readonly ObjectCreatedNotification notification;

        public IssueCommandHandler(NotificationService notificationService, ObjectCreatedNotification notification)
        {
            this.notificationService = notificationService;
            this.notification = notification;
        }

        protected override void ProcessCommand(IssueCommand command)
        {
            var action = Service.ProcessIssueCommand(command, command.CommentId, command.CommentCommentId, command.RelatedUserId);

            if ((command.ActionType.In(ActionTypes.IssueCreated, ActionTypes.IssueEdited)) && command.SendMail)
            {
                notificationService.SendObjectCreatedNotification(notification, action, NotificationTypes.IssueCreated, command.Link);
            }
        }
    }
}
