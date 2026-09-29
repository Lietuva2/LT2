using System;
using System.Linq;
using System.Text;
using Bus.Commands;
using Data.Enums;
using Data.Infrastructure.Sessions;
using Data.MongoDB;
using Framework.Infrastructure.Logging;
using MongoDB.Bson;

using Services.ModelServices;

namespace Bus.CommandHandler
{
    public class VoteCancelledCommandHandler : CommandHandlerBase<VoteCancelledCommand, ActionService>
    {
        protected override void ProcessCommand(VoteCancelledCommand command)
        {
            Service.DeleteVoteAction(command.ObjectId, command.UserId);
        }
    }
}
