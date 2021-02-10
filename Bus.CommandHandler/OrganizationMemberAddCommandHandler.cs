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
    public class OrganizationMemberAddCommandHandler : ActionCommandHandler<OrganizationMemberAddCommand>
    {
        protected override void ProcessCommand(OrganizationMemberAddCommand command)
        {
            Service.ProcessOrganizationCommand(command, command.AddedUserId);
        }
    }
}
