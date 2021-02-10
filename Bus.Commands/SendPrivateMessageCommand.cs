using System;
using System.Linq;
using System.Text;
using Data.Enums;
using Framework.Bus;

namespace Bus.Commands
{
    public class SendPrivateMessageCommand : Command
    {
        public int? UserDbId { get; set; }
        public int? UserFromDbId { get; set; }
        public string AddressFrom { get; set; }
        public string Message { get; set; }
        public string Subject { get; set; }
    }
}
