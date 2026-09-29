using System;
using Data.Enums;
using Framework.Bus;


namespace Bus.Commands
{
    /// <summary>
    /// Base command class.
    /// </summary>
    public class ActionCommand : Command
    {
        public ActionTypes ActionType { get; set; }
        public string Text { get; set; }
        public string UserId { get; set; }
        public string Link { get; set; }
        public string ObjectId { get; set; }
        public bool IsPrivate { get; set; }
    }
}
