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
using Services.Notifications;

namespace Bus.CommandHandler
{
    public class ActionCommandHandler<TCommand> : CommandHandlerBase<TCommand, ActionService>
        where TCommand : ActionCommand
    {
    }
}
