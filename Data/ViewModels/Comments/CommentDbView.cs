using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using Data.Enums;
using Data.ViewModels.Voting;
using Framework.Infrastructure.Storage;

namespace Data.ViewModels.Comments
{
    public class CommentDbView : BaseCommentDbView
    {
        public IEnumerable<string> CommentCommentIds { get; set; }
        public IEnumerable<CommentCommentDbView> CommentComments { get; set; }
    }
}
