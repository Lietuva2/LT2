using Bus.Commands;
using Framework.Infrastructure.Logging;

using Services.ModelServices;

namespace Bus.CommandHandler
{
    public class IdeaVersionViewedCommandHandler : CommandHandlerBase<IdeaVersionViewedCommand, ActionService>
    {
        protected override void ProcessCommand(IdeaVersionViewedCommand command)
        {
            Service.SetIdeaVersionViewed(command.VersionId, command.UserDbId);
        }
    }
}
