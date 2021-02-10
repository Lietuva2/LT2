using System;
using System.Collections.Generic;
using Data.Enums;
using Framework.Bus;


namespace Bus.Commands
{
    public class UserConfirmedCommand : Command
    {
        public int UserId { get; set; }
        public int PersonCodeStatus { get; set; }
    }
}
