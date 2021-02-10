using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using Data.Enums;
using Data.ViewModels.Base;
using Data.ViewModels.Voting;
using Framework.Infrastructure.Storage;

namespace Data.ViewModels.Comments
{
    public class CommentView
    {
        public string Id { get; set; }
        public string AuthorObjectId { get; set; }
        [Required]
        public string CommentText { get; set; }
        public string AuthorFullName { get; set; }
        public DateTime CommentDate { get; set; }
        public string CommentDateString { get { return CommentDate.ToString("yyyy-MM-dd HH:mm"); } }
        public List<CommentView> Comments { get; set; }
        public string ParentId { get; set; }
        public string EntryId { get; set; }
        public string RelatedVersion { get; set; }
        public string VersionId { get; set; }
        public ForAgainst ForAgainst { get; set; }
        public VotingStatisticsViewModel VotingStatistics { get; set; }
        public string ButtonText { get; set; }
        public bool IsNewsFeed { get; set; }
        public EntryTypes EntryType { get; set; }
        public bool IsCreatedByCurrentUser { get; set; }
        public bool IsCommentable { get; set; }
        public bool IsHidden { get; set; }
        public EmbedModel Embed { get; set; }
        public SubscribeModel Subscribe { get; set; }
        public SubscribeModel SubscribeMain { get; set; }
        public string Number { get; set; }
        public string ProfilePictureThumbId { get; set; }

        public Liking Liking { get; set; }

        public CommentView()
        {
            Comments = new List<CommentView>();
            Liking = new Liking();
            Embed = new EmbedModel();
        }
    }

    public class Liking
    {
        public string EntryId { get; set; }
        public string ParentId { get; set; }
        public string CommentId { get; set; }
        public string CommentCommentId { get; set; }
        public string LikesCount { get; set; }
        public bool IsLikeVisible { get; set; }
        public bool IsUnlikeVisible { get; set; }
        public EntryTypes Type { get; set; }
    }
}
