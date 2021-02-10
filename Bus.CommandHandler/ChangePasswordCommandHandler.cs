﻿using System;
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
    public class ChangePasswordHandler : CommandHandlerBase<ChangePasswordCommand, NotificationService>
    {
        public readonly ChangePasswordNotification notification;

        public ChangePasswordHandler(ChangePasswordNotification notification)
        {
            this.notification = notification;
        }

        /// <summary>
        /// Handles completed virtual machine command.
        /// </summary>
        /// <param name="command">Completed virtual machine command.</param>
        protected override void ProcessCommand(ChangePasswordCommand command)
        {
            Service.SendChangePasswordNotification(notification, command);
        }
    }
}
