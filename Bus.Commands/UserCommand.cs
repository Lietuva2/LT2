using System;
using Data.Enums;


namespace Bus.Commands
{
    public class UserCommand : ActionCommand
    {
        public string CommentId { get; set; }
        public string CommentCommentId { get; set; }
        public string RelatedUserId { get; set; }
    }
}
