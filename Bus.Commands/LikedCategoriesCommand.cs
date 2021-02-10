using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Data.Enums;
using Framework.Bus;

namespace Bus.Commands
{
    public class LikedCategoriesCommand : Command
    {
        public int UserDbId { get; set; }
        public List<short> CategoryIds { get; set; }
    }
}
