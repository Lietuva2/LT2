using System;
using System.Collections.Generic;
using Data.Enums;


namespace Bus.Commands
{
    public class IdeaCommand : ActionCommand
    {
        public string VersionId { get; set; }
        public string CommentId { get; set; }
        public string CommentCommentId { get; set; }
        public string RelatedUserId { get; set; }
        public string Subject { get; set; }
        public bool SendMail { get; set; }
    }
}
