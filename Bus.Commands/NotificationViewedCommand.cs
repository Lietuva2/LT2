using System;
using System.Collections.Generic;
using Data.Enums;
using Framework.Bus;


namespace Bus.Commands
{
    public class NotificationViewedCommand : Command
    {
        public int NotifyUserDbId { get; set; }
        public int LastViewedId { get; set; }

        public NotificationViewedCommand()
        {
        }
    }
}
