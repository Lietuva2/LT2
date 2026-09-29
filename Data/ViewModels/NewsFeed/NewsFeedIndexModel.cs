using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Data.ViewModels.Base;
using Data.ViewModels.Problem;
using Framework.Lists;

namespace Data.ViewModels.NewsFeed
{
    public class NewsFeedIndexModel
    {
        public List<UrgentMessageModel> UrgentMessages { get; set; }
        public ExpandableList<NewsFeedItemModel> List { get; set; }
        public ProblemIndexModel ProblemInput { get; set; }

        public NewsFeedIndexModel()
        {
            UrgentMessages = new List<UrgentMessageModel>();
        }
    }
}