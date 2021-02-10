using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Data.EF.Voting;
using Data.ViewModels.Base;

namespace Data.ViewModels.Idea
{
    public class RelatedIdeaDialogModel
    {
        public string Id { get; set; }
        public List<Data.ViewModels.Base.RelatedIdeaListItem> RelatedIdeas { get; set; }

        public RelatedIdeaDialogModel()
        {
            RelatedIdeas = new List<RelatedIdeaListItem>();
        }
    }
}
