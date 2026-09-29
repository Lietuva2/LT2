using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Data.ViewModels.Base;
using Data.ViewModels.Idea;
using Data.ViewModels.Voting;

namespace Data.ViewModels.NewsFeed
{
    public class DashboardListModel : SimpleListModel
    {
        public string Text { get; set; }
        public VotingStatisticsViewModel VotingProgress { get; set; }
        public ProgressViewModel IdeaProgress { get; set; }
    }
}
