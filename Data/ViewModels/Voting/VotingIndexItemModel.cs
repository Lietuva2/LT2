using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Data.Enums;
using Framework;
using Framework.Infrastructure.Storage;

namespace Data.ViewModels.Voting
{
    public class VotingIndexItemModel
    {
        public string Id { get; set; }
        public int DbId { get; set; }
        public string Subject { get; set; }
        public string Summary { get; set; }
        public DateTime? Deadline { get; set; }
        public string TimeLeft { get; set; }
        public string DocumentUrl { get; set; }
        public List<TextValue> Categories { get; set; }
        public List<int> CategoryIds { get; set; }
        public int VotesCount { get; set; }
        public int CommentsCount { get; set; }
        public int ViewsCount { get; set; }
        public int ActivityRank { get; set; }
        public string Municipality { get; set; }
        public ForAgainst? Vote { get; set; }
        public ForAgainst? AdditionalVote { get; set; }
        public bool IsPrivate { get; set; }
        public VotingStatisticsViewModel Progress { get; set; }
        public string Text { get; set; }
        public bool VoteButtonVisible { get; set; }

        public VotingIndexItemModel()
        {
            Categories = new List<TextValue>();
            Vote = ForAgainst.Neutral;
        }
    }
}