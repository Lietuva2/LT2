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
    public class UserCommandHandler : ActionCommandHandler<UserCommand>
    {
        protected override void ProcessCommand(UserCommand command)
        {
            Service.ProcessUserCommand(command, command.RelatedUserId, command.CommentId, command.CommentCommentId);
        }
    }
}
