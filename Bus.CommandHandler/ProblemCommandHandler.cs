using Bus.Commands;
using Framework.Infrastructure.Logging;

using Services.ModelServices;
using Services.Notifications;

namespace Bus.CommandHandler
{
    public class ProblemCommandHandler : ActionCommandHandler<ProblemCommand>
    {
        /// <summary>
        /// Handles completed virtual machine command.
        /// </summary>
        /// <param name="command">Completed virtual machine command.</param>
        protected override void ProcessCommand(ProblemCommand command)
        {
            Service.ProcessProblemCommand(command, command.RelatedObjectId, command.RelatedUserId, command.RelatedSubject, command.RelatedLink);
        }
    }
}
