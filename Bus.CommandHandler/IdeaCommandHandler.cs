using System;
using System.Linq;
using System.Text;
using Bus.Commands;
using Data.Enums;
using Data.Infrastructure.Sessions;
using Data.MongoDB;
using Framework.Infrastructure.Logging;
using MongoDB.Bson;

using Services.Classes;
using Services.ModelServices;
using Services.Notifications;

namespace Bus.CommandHandler
{
    public class IdeaCommandHandler : ActionCommandHandler<IdeaCommand>
    {
        public readonly NotificationService notificationService;
        public readonly ObjectCreatedNotification notification;
        public readonly IdeaResolvedNotification resolvedNotification;

        public IdeaCommandHandler(NotificationService notificationService, ObjectCreatedNotification notification, IdeaResolvedNotification resolvedNotification)
        {
            this.notificationService = notificationService;
            this.notification = notification;
            this.resolvedNotification = resolvedNotification;
        }

        /// <summary>
        /// Handles completed virtual machine command.
        /// </summary>
        /// <param name="command">Completed virtual machine command.</param>
        protected override void ProcessCommand(IdeaCommand command)
        {
            var action = Service.ProcessIdeaCommand(command, command.Subject, command.VersionId, command.CommentId, command.CommentCommentId, command.RelatedUserId);

            if ((command.ActionType == ActionTypes.IdeaCreated || command.ActionType == ActionTypes.IdeaEdited) && command.SendMail)
            {
                notificationService.SendObjectCreatedNotification(notification, action, NotificationTypes.IdeaCreated, command.Link);
            }
        }
    }
}
