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
    public class LikedCategoriesCommandHandler : CommandHandlerBase<LikedCategoriesCommand, ActionService>
    {
        protected override void ProcessCommand(LikedCategoriesCommand command)
        {
            Service.ProcessCategoryAction(ActionTypes.LikedCategory, command.MessageDate, command.UserDbId, command.CategoryIds);
        }
    }
}
