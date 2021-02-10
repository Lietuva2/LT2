using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Data.Enums;
using Data.ViewModels.Base;
using Data.ViewModels.Comments;
using Data.ViewModels.Problem;
using Framework.Infrastructure.Storage;

namespace Data.ViewModels.NewsFeed
{
    public class NewsFeedItemModel
    {
        public int Id { get; set; }
        public int ActionTypeId { get; set; }
        public string ActionTypeName { get; set; }
        public DateTime Date { get; set; }
        public string UserFullName { get; set; }
        public int UserDbId { get; set; }
        public string UserObjectId { get; set; }
        public string ObjectId { get; set; }
        public List<UserLinkModel> Users { get; set; }
        public int? UserCount { get; set; }
        public string Subject { get; set; }
        public string ActionDescription { get; set; }
        public string TimePassed { get; set; }
        public List<string> CategoryNames { get; set; }
        public string Text { get; set; }
        public string RawText { get; set; }
        public string RelatedUserObjectId { get; set; }
        public string RelatedUserFullName { get; set; }
        public int? RelatedUserCount { get; set; }
        public List<UserLinkModel> RelatedUsers { get; set; }
        public int NewsFeedTypeId { get; set; }
        public string RelatedObjectId { get; set; }
        public string RelatedRelatedObjectId { get; set; }
        //public Liking Liking { get; set; }
        public CommentView Comment { get; set; }
        public bool IsRead { get; set; }
        public string Link { get; set; }
        public string RelatedLink { get; set; }
        public string RelatedSubject { get; set; }
        public string OrganizationId { get; set; }
        public string OrganizationName { get; set; }
        public bool IsPrivate { get; set; }
        public List<NewsFeedItemModel> InnerList { get; set; }
        public EntryTypes? EntryType { get { return (EntryTypes?)EntryTypeId; } }
        public string EntryTypeTooltip { get; set; }
        public int? EntryTypeId { get; set; }
        public ProblemIndexItemModel Problem { get; set; }
        public int? MunicipalityId { get; set; }
        public int? Reputation { get; set; }
        public string ProfilePictureThumbId { get; set; }
        public int? GroupId { get; set; }
        public IEnumerable<short> CategoryIds { get; set; }

        public NewsFeedItemModel()
        {
            CategoryNames = new List<string>();
            Users = new List<UserLinkModel>();
            IsRead = true;
        }
    }
}