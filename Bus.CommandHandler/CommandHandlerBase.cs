using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using Data.Enums;
using Data.Infrastructure.Sessions;
using Data.MongoDB;
using Framework.Bus;
using Framework.Infrastructure.Logging;

using Ninject;
using Services.ModelServices;
using Services.Notifications;

namespace Bus.CommandHandler
{
    public class CommandHandlerBase<TCommand, TService> : CommandHandler<TCommand>
        where TCommand : Command
    {
        [Inject]
        public TService Service { get; set; }

        public override void Handle(TCommand command)
        {
            var xmlSerializer = new XmlSerializer(command.GetType());
            var textWriter = new StringWriter();

            xmlSerializer.Serialize(textWriter, command);

            Logger.Information(string.Format("Received {0}: {1}", command.GetType().Name, textWriter));

            ProcessCommand(command);

            Logger.Information(string.Format("{0}, id={1} successfully completed", command.GetType().Name, command.MessageId));
        }

        protected virtual void ProcessCommand(TCommand command)
        {
        }
    }
}
