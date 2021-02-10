using System;
using System.Collections.Generic;
using Data.Enums;
using Framework.Bus;


namespace Bus.Commands
{
    public class IdeaResolvedCommand : Command
    {
        public string IdeaId { get; set; }
        public string Link { get; set; }
    }
}
