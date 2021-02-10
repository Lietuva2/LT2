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
    public class CommentDeletedCommandHandler : CommandHandlerBase<CommentDeletedCommand, ActionService>
    {
        /// <summary>
        /// Handles completed virtual machine command.
        /// </summary>
        /// <param name="command">Completed virtual machine command.</param>
        protected override void ProcessCommand(CommentDeletedCommand command)
        {
            Service.DeleteCommentActions(command.CommentId, command.EntryType, command.UserObjectId);
        }
    }
}
