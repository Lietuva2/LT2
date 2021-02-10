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
    public class UserCreatedCommandHandler : ActionCommandHandler<UserCreatedCommand>
    {
        public readonly NotificationService notificationService;
        public readonly ConfirmNotification notification;

        public UserCreatedCommandHandler(NotificationService notificationService, ConfirmNotification notification)
        {
            this.notificationService = notificationService;
            this.notification = notification;
        }

        protected override void ProcessCommand(UserCreatedCommand command)
        {
            Service.ProcessUserCommand(command);
            notificationService.SendUserCreatedNotification(notification, command);
        }
    }
}
