using System;
using System.Collections.Generic;
using Data.Enums;
using Framework.Bus;


namespace Bus.Commands
{
    public class CommentUnlikedCommand : Command
    {
        public string ObjectId { get; set; }
        public string CommentId { get; set; }
        public EntryTypes EntryType { get; set; }
        public string UserObjectId { get; set; }
    }
}
