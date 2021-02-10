using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Data.ViewModels.Base;
using Data.ViewModels.Idea;
using Data.ViewModels.Voting;
using Framework.Lists;

namespace Data.ViewModels.NewsFeed
{
    public class StartPageModel
    {
        public IdeaIndexModel Ideas { get; set; }
        public VotingIndexModel BestIssues { get; set; }
        public VotingResultsModel FinishedIssues { get; set; }
        public List<DashboardListModel> ActiveProjects { get; set; }
        public List<DashboardListModel> ActiveOrganizations { get; set; }
        public ExpandableList<NewsFeedItemModel> NewsFeed { get; set; }
        public int MembersCount { get; set; }
        public string MembersCountString { get; set; }
        public string ImageUrl { get; set; }

        public StartPageModel()
        {
        }
    }
}