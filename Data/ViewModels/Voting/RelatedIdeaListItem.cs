using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Data.Enums;

namespace Data.ViewModels.Base
{
    public class RelatedIdeaListItem : ListItem
    {
        public bool ChangeState { get; set; }
        public IdeaStates State { get; set; }
        public int SupportersCount { get; set; }
        public InitiativeTypes? InitiativeType { get; set; }
        public bool IsVisible { get; set; }

        public RelatedIdeaListItem()
        {
            ChangeState = true;
        }
    }
}
