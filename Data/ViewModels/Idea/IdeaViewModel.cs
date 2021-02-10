using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;
using Data.Enums;
using Data.ViewModels.Base;
using Data.ViewModels.Comments;
using Framework.Mvc.DataAnnotations;
using Hyper.ComponentModel;

namespace Data.ViewModels.Idea
{
    [TypeDescriptionProvider(typeof(HyperTypeDescriptionProvider))]
    public class IdeaViewModel
    {
        public string Id { get; set; }
        public int DbId { get; set; }
        public string Subject { get; set; }
        public List<WikiVersionModel> Versions { get; set; }
        public WikiVersionModel CurrentVersion { get; set; }
        public bool IsClosed { get; set; }
        public List<UserLinkModel> InvolvedExprets { get; set; }
        public string DocumentUrl { get; set; }
        public List<UrlViewModel> Urls { get; set; }
        public List<int> CategoryIds { get; set; }
        public List<string> Categories { get; set; }
        public string UserObjectId { get; set; }
        //public string UserFullName { get; set; }
        public CommentsModel Comments { get; set; }
        public int TotalSupporters { get; set; }
        public int TotalConfirmedSupporters { get; set; }
        public int TotalUnconfirmedSupporters { get; set; }
        [AllowHtml]
        public string Aim { get; set; }
        [AllowHtml]
        public string Resolution { get; set; }
        public bool IsEditable { get; set; }
        public bool IsCurrentUserInvolved { get; set; }
        public bool IsContributable { get; set; }
        public IdeaStates State { get; set; }
        public bool IsJoinable { get; set; }
        public string ProjectId { get; set; }
        [AllowHtml]
        public string StateDescription { get; set; }
        public List<ListItem> RelatedIssues { get; set; }
        public List<RelatedIdeaListItem> RelatedIdeas { get; set; }
        public IEnumerable<SelectListItem> VersionSelectList { get; set; }
        public string Municipality { get; set; }
        public ObjectVisibility Visibility { get; set; }
        public bool IsPrivateToOrganization { get { return Visibility != ObjectVisibility.Public; } }
        public string OrganizationId { get; set; }
        public bool IsDraft { get; set; }
        public string FinalVersionId { get; set; }
        public List<SimpleListModel> Problems { get; set; }
        public int? RequiredVotes { get; set; }
        public bool IsLikedByUniqueCurrentUser { get; set; }
        public ProgressViewModel Progress { get; set; }
        public bool IsMailSendable { get; set; }
        public DateTime? Deadline { get; set; }
        public TimeSpan? DeadlineTime { get; set; }
        public InitiativeTypes? InitiativeType { get; set; }
        public List<UrlViewModel> Attachments { get; set; }
        public bool IsImpersonal { get; set; }
        public string ShortLink { get; set; }
        public SubscribeModel Subscribe { get; set; }
        public string OfficialUrl { get; set; }
        public bool WasLiked { get; set; }
        public bool AllowPublicAlternativeIdeas { get; set; }
        public bool PromoteToFrontPage { get; set; }
        public bool IsLikeable {get
        {
            return (State != IdeaStates.Resolved && !CurrentVersion.IsLikedByCurrentUser) || (State == IdeaStates.Resolved && !IsLikedByUniqueCurrentUser);
        }}

        public IdeaViewModel()
        {
            Versions = new List<WikiVersionModel>();
            InvolvedExprets = new List<UserLinkModel>();
            RelatedIssues = new List<ListItem>();
            RelatedIdeas = new List<RelatedIdeaListItem>();
            Problems = new List<SimpleListModel>();
            Attachments = new List<UrlViewModel>();
        }
    }
}