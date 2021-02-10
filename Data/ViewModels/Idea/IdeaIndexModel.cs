using System.Collections.Generic;
using System.Web.Mvc;
using Framework.Lists;

namespace Data.ViewModels.Idea
{
    public class IdeaIndexModel
    {
        public ExpandableList<IdeaIndexItemModel> Items { get; set; }
        public int IdeaListView { get; set; }
        public int IdeaListSort { get; set; }
        public IEnumerable<SelectListItem> SelectedCategories { get; set; }
        public IEnumerable<SelectListItem> SelectedStates { get; set; }
        public int TotalCount { get; set; }
        public bool NoMunicipalities { get; set; }
        public string OrganizationId { get; set; }
        public bool IsEditable { get; set; }
    }
}