using System;
using System.Linq;
using System.Text;
using Bus.Commands;
using Data.Enums;
using Data.Infrastructure.Sessions;
using Data.MongoDB;
using Framework.Infrastructure.Logging;
using MongoDB.Bson;

using Services.Classes;
using Services.ModelServices;
using Services.Notifications;

namespace Bus.CommandHandler
{
    public class IdeaResolvedCommandHandler : CommandHandlerBase<IdeaResolvedCommand, NotificationService>
    {
        public readonly IdeaResolvedNotification resolvedNotification;

        public IdeaResolvedCommandHandler(IdeaResolvedNotification resolvedNotification)
        {
            this.resolvedNotification = resolvedNotification;
        }

        /// <summary>
        /// Handles completed virtual machine command.
        /// </summary>
        /// <param name="command">Completed virtual machine command.</param>
        protected override void ProcessCommand(IdeaResolvedCommand command)
        {
            Service.SendIdeaResolvedNotification(resolvedNotification, command.IdeaId, command.Link);
        }
    }
}
