using System;
using System.Collections.Generic;
using Data.Enums;
using Framework.Bus;


namespace Bus.Commands
{
    public class ToDoUnfinishedCommand : Command
    {
        public string ToDoId { get; set; }
    }
}
