using System;
using System.Collections.Generic;
using System.Web.Mvc;
using Data.Enums;
using Data.ViewModels.Base;
using Data.ViewModels.Comments;
using Framework;
using Framework.Lists;

namespace Data.ViewModels.Problem
{
    public class ProblemIndexItemModel
    {
        public string Id { get; set; }
        [AllowHtml]
        public string Text { get; set; }
        public DateTime Date { get; set; }
        public List<TextValue> Categories { get; set; }
        public IEnumerable<int> CategoryIds { get; set; }
        public string Municipality { get; set; }
        public int? MunicipalityId { get; set; }
        public string UserObjectId { get; set; }
        public string UserFullName { get; set; }
        public ExpandableList<CommentView> Comments { get; set; }
        public IEnumerable<CommentDbView> DbComments { get; set; }
        public int CommentsCount { get; set; }
        public IEnumerable<ProblemIdeaListModel> RelatedIdeas { get; set; }
        public bool CanDelete { get; set; }
        public VoteResultModel Votes { get; set; }
        public bool RenderCollapsed { get; set; }
        public string OrganizationId { get; set; }
        public string OrganizationName { get; set; }
        public EmbedModel Embed { get; set; }
        public SubscribeModel Subscribe { get; set; }
        public bool IsPrivate { get; set; }
        public string ProfilePictureThumbId { get; set; }

        public ProblemIndexItemModel()
        {
            Categories = new List<TextValue>();
            RelatedIdeas = new List<ProblemIdeaListModel>();
            Votes = new VoteResultModel();
            Embed = new EmbedModel();
        }
    }
}