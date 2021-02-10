using System;
using System.Linq;
using System.Text;
using Bus.Commands;
using Data.Enums;
using Data.Infrastructure.Sessions;
using Data.MongoDB;
using Framework.Infrastructure.Logging;

using Services.ModelServices;

namespace Bus.CommandHandler
{
    public class RelatedUserCommandHandler : ActionCommandHandler<RelatedUserCommand>
    {
        protected override void ProcessCommand(RelatedUserCommand command)
        {
            Service.ProcessUserCommand(command, command.RelatedUserId);
        }
    }
}
