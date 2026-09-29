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
    public class UserConfirmedCommandHandler : CommandHandlerBase<UserConfirmedCommand, NotificationService>
    {
        public readonly UserConfirmedNotification notification;

        public UserConfirmedCommandHandler(UserConfirmedNotification notification)
        {
            this.notification = notification;
        }

        /// <summary>
        /// Handles completed virtual machine command.
        /// </summary>
        /// <param name="command">Completed virtual machine command.</param>
        protected override void ProcessCommand(UserConfirmedCommand command)
        {
            Service.SendUserConfirmedNotification(notification, command);
        }
    }
}
