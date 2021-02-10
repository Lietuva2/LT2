using Data.Enums;
using Framework.Bus;

namespace Bus.Commands
{
    public class SendObjectCreatedNotificationCommand : Command
    {
        public string ObjectId { get; set; }
        public string Link { get; set; }
        public NotificationTypes Type { get; set; }
        public string UserObjectId { get; set; }

        public SendObjectCreatedNotificationCommand()
        {
        }
    }
}
