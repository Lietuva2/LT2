using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Data.Enums;

namespace Bus.Commands
{
    public class RelatedUserCommand : ActionCommand
    {
        public string RelatedUserId { get; set; }
    }
}
