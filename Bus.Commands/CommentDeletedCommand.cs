using Data.Enums;
using Framework.Bus;

namespace Bus.Commands
{
    public class CommentDeletedCommand : Command
    {
        public string CommentId { get; set; }
        public EntryTypes EntryType { get; set; }
        public string UserObjectId { get; set; }

        public CommentDeletedCommand()
        {
        }
    }
}
