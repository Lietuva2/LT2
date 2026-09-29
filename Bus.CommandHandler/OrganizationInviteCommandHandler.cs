using System;
using System.Linq;
using System.Text;
using Bus.Commands;
using Data.Enums;
using Data.Infrastructure.Sessions;
using Data.MongoDB;
using Framework.Infrastructure.Logging;

using Services.ModelServices;
using Services.Notifications;

namespace Bus.CommandHandler
{
    public class OrganizationInviteCommandHandler : CommandHandlerBase<OrganizationInviteCommand, NotificationService>
    {
        public readonly InviteNotification notification;

        public OrganizationInviteCommandHandler(InviteNotification notification)
        {
            this.notification = notification;
        }

        /// <summary>
        /// Handles completed virtual machine command.
        /// </summary>
        /// <param name="command">Completed virtual machine command.</param>
        protected override void ProcessCommand(OrganizationInviteCommand command)
        {
            Service.SendOrganizationInviteNotification(notification, command);
        }
    }
}
