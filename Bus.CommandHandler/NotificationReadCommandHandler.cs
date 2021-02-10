using System;
using System.Linq;
using System.Text;
using Bus.Commands;
using Data.Enums;
using Data.Infrastructure.Sessions;
using Data.MongoDB;
using Framework.Infrastructure.Logging;

using Ninject;
using Services.ModelServices;

namespace Bus.CommandHandler
{
    public class NotificationReadCommandHandler : CommandHandlerBase<NotificationViewedCommand, ActionService>
    {
        protected override void ProcessCommand(NotificationViewedCommand command)
        {
            Service.SetNotificationViewed(command);
        }
    }
}
