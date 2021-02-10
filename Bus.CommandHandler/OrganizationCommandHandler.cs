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
    public class OrganizationCommandHandler : ActionCommandHandler<OrganizationCommand>
    {
        protected override void ProcessCommand(OrganizationCommand command)
        {
            Service.ProcessOrganizationCommand(command);
        }
    }
}
