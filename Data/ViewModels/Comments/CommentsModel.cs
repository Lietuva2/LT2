using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using Data.Enums;
using Data.ViewModels.Voting;
using Framework.Infrastructure.Storage;
using Framework.Lists;

namespace Data.ViewModels.Comments
{
    public class CommentsModel
    {
        public string EntryId { get; set; }
        public ExpandableList<CommentView> Comments { get; set; }
        public EntryTypes Type { get; set; }
        public Dictionary<ForAgainst, int> CommentCounts { get; set; }

        public CommentsModel()
        {
            CommentCounts = new Dictionary<ForAgainst, int>();
        }
    }
}
