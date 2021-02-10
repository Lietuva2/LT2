using Data.Enums;
using Framework.Bus;

namespace Bus.Commands
{
    public class ToDoDeletedCommand : Command
    {
        public string ToDoId { get; set; }
    }
}
