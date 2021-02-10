using System;
using System.Collections.Generic;
using Data.Enums;
using Data.MongoDB;
using Framework;

namespace Data.ViewModels.Idea
{
    public class IdeaIndexItemModel
    {
        public string Id { get; set; }
        public string Subject { get; set; }
        public string Summary { get; set; }
        public bool IsClosed { get; set; }
        public List<TextValue> Categories { get; set; }
        public List<int> CategoryIds { get; set; }
        public int CommentsCount { get; set; }
        public int ViewsCount { get; set; }
        public int VersionsCount { get; set; }
        public int ActivityRank { get; set; }
        public int TotalSupporters { get; set; }
        public int TotalConfirmedSupporters { get; set; }
        public int TotalUnconfirmedSupporters { get; set; }
        public List<WikiVersionModel> Versions { get; set; }
        public IdeaStates State { get; set; }
        public string Municipality { get; set; }
        public string AbsoluteUrl { get; set; }
        public bool IsBold { get; set; }
        public bool IsDraft { get; set; }
        public bool IsPrivate { get; set; }
        public bool SupportedByCurrentUser { get; set; }
        public string Text { get; set; }
        public ProgressViewModel Progress { get; set; }
        public DateTime? Deadline { get; set; }
        public InitiativeTypes? InitiativeType { get; set; }

        public IdeaIndexItemModel()
        {
            Categories = new List<TextValue>();
        }
    }
}