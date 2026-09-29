using System;
using System.Linq;
using System.Text;
using Data.Enums;
using Framework.Bus;

namespace Bus.Commands
{
    public class ChangePasswordCommand : Command
    {
        public string UserName { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
    }
}
