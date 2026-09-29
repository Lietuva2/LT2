using System;
using System.Linq;
using System.Text;
using Data.Enums;
using Framework.Bus;

namespace Bus.Commands
{
    public class EmailChangedCommand : Command
    {
        public int UserDbId { get; set; }
        public string Email { get; set; }
    }
}
