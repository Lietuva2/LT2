using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;
using Data.Enums;
using Data.ViewModels.Base;
using Data.ViewModels.Comments;
using Framework.Lists;
using Hyper.ComponentModel;
using Framework.Infrastructure.Storage;

namespace Data.ViewModels.Voting
{
    [TypeDescriptionProvider(typeof(HyperTypeDescriptionProvider))]
    public class VotingViewModel
    {
        public string Id { get; set; }
        public int DbId { get; set; }
        public string Subject { get; set; }
        public string Summary { get; set; }
        public List<WikiVersionModel> Versions { get; set; }
        public DateTime? Deadline { get; set; }
        public string TimeLeft { get; set; }
        public string DocumentUrl { get; set; }
        public List<string> Categories { get; set; }
        public bool IsCategoryLiked { get; set; }
        public string RegisteredBy { get; set; }
        public string UserObjectId { get; set; }
        public string UserFullName { get; set; }
        public ExpandableList<CommentView> PositiveComments { get; set; }
        public ExpandableList<CommentView> NegativeComments { get; set; }
        public int PositiveCommentsCount { get; set; }
        public int NegativeCommentsCount { get; set; }
        public ForAgainst? Vote { get; set; }
        public ForAgainst? AdditionalVote { get; set; }
        public string VotedString { get; set; }
        public bool IsVotingFinished { get; set; }
        public int VotesCount { get; set; }
        public int? SupportPercentage { get; set; }
        public int SupportingVotesCount { get; set; }
        public int NonSupportingVotesCount { get; set; }
        public int NeutralVotesCount { get; set; }
        public int SupportingAdditionalVotesCount { get; set; }
        public int NonSupportingAdditionalVotesCount { get; set; }
        public int NeutralAdditionalVotesCount { get; set; }
        public bool IsEditable { get; set; }
        public bool IsDeletable { get; set; }
        public ForAgainst OfficialVote { get; set; }
        public List<RelatedIdeaListItem> RelatedIdeas { get; set; }
        public string Municipality { get; set; }
        public string OrganizationId { get; set; }
        public string OrganizationName { get; set; }
        public bool IsPrivateToOrganization { get { return Visibility != ObjectVisibility.Public; } }
        public ObjectVisibility Visibility { get; set; }
        public string OfficialVotingDescription { get; set; }
        public CommentView CommentInput { get; set; }
        public List<UrlViewModel> Urls { get; set; }
        public List<SimpleListModel> Problems { get; set; }
        public bool IsMailSendable { get; set; }
        public DateTime Date { get; set; }
        public string ShortLink { get; set; }
        public SubscribeModel Subscribe { get; set; }
        public bool AllowSummaryWiki { get; set; }
        public bool AllowNeutralVotes { get; set; }

        public class WikiVersionModel
        {
            public string ObjectId { get; set; }
            public string UserObjectId { get; set; }
            public string UserFullName {get; set; }
            public string VersionName { get; set; }
            public string VersionText { get; set; }
        }

        public VotingViewModel()
        {
            Versions = new List<WikiVersionModel>();
            Categories = new List<string>();
            Vote = ForAgainst.Neutral;
            OfficialVote = ForAgainst.Neutral;
            RelatedIdeas = new List<RelatedIdeaListItem>();
        }
    }
}