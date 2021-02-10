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
    public class ToDoUnfinishedCommandHandler : CommandHandlerBase<ToDoUnfinishedCommand, ActionService>
    {
        protected override void ProcessCommand(ToDoUnfinishedCommand command)
        {
            Service.UnfinishToDo(command.ToDoId);
        }
    }
}
