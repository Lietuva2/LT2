using System;
using System.Linq;
using System.Text;
using Bus.Commands;
using Data.Enums;
using Data.Infrastructure.Sessions;
using Framework.Hashing;
using Framework.Infrastructure.Logging;
using MongoDB.Bson;

using Services.ModelServices;
using Services.Notifications;

namespace Bus.CommandHandler
{
    public class EmailChangedCommandHandler : CommandHandlerBase<EmailChangedCommand, NotificationService>
    {
        public readonly ConfirmNotification notification;

        public EmailChangedCommandHandler(ConfirmNotification notification)
        {
            this.notification = notification;
        }

        /// <summary>
        /// Handles completed virtual machine command.
        /// </summary>
        /// <param name="command">Completed virtual machine command.</param>
        protected override void ProcessCommand(EmailChangedCommand command)
        {
            Service.SendEmailChangedNotification(notification, command);
        }
    }
}
