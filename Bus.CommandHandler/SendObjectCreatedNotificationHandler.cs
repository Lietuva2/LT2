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
    public class SendObjectCreatedNotificationCommandHandler : CommandHandlerBase<SendObjectCreatedNotificationCommand, NotificationService>
    {
        public readonly ObjectCreatedNotification notification;

        public SendObjectCreatedNotificationCommandHandler(ObjectCreatedNotification notification)
        {
            this.notification = notification;
        }

        /// <summary>
        /// Handles completed virtual machine command.
        /// </summary>
        /// <param name="command">Completed virtual machine command.</param>
        protected override void ProcessCommand(SendObjectCreatedNotificationCommand command)
        {
            Service.SendObjectCreatedNotification(notification, command.Type, command.ObjectId, command.Link);
        }
    }
}
