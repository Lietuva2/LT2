using System;
using System.Collections.Generic;
using Data.Enums;


namespace Bus.Commands
{
    public class ProblemCommand : ActionCommand
    {
        public string RelatedObjectId { get; set; }
        public string RelatedRelatedObjectId { get; set; }
        public string RelatedUserId { get; set; }
        public string RelatedSubject { get; set; }
        public string RelatedLink { get; set; }
    }
}
