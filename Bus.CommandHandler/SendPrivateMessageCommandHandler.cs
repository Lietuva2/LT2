using System;
using System.Configuration;
using System.Linq;
using System.Text;
using Bus.Commands;
using Data.Enums;
using Data.Infrastructure.Sessions;
using Framework.Hashing;
using Framework.Infrastructure.Logging;
using Framework.Strings;
using MongoDB.Bson;

using Services.ModelServices;
using Services.Notifications;

namespace Bus.CommandHandler
{
    public class SendPrivateMessageCommandHandler : CommandHandlerBase<SendPrivateMessageCommand, NotificationService>
    {
        public readonly PrivateMessage message;

        public SendPrivateMessageCommandHandler(PrivateMessage message)
        {
            this.message = message;
        }

        /// <summary>
        /// Handles completed virtual machine command.
        /// </summary>
        /// <param name="command">Completed virtual machine command.</param>
        protected override void ProcessCommand(SendPrivateMessageCommand command)
        {
            Service.SendPrivateMessage(message, command, ConfigurationManager.AppSettings["ReplyTo"]);
        }
    }
}
