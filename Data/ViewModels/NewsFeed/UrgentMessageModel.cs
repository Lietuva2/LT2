using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Framework.Infrastructure.Storage;

namespace Data.ViewModels.NewsFeed
{
    public class UrgentMessageModel
    {
        public string ObjectId { get; set; }
        public string Subject { get; set; }
        public bool Highlight { get; set; }
        public int? Count { get; set; }
        public UrgentMessageTypes Type { get; set; }
        public List<UrgentMessageItemModel> Items { get; set; }
    }

    public enum UrgentMessageTypes
    {
        UnreadIdeaVersions = 1,
        MyProjects = 2,
        RandomIdeas = 3,
        RandomIssues = 4,
        RealizedIdeas = 5,
        MyOrganizations = 6
    }
}
