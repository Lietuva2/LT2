using System.Collections.Generic;
using System.Web.Mvc;
using Data.ViewModels.Base;
using Framework.Lists;

namespace Data.ViewModels.Problem
{
    public class ProblemIndexModel
    {
        public ExpandableList<ProblemIndexItemModel> Items { get; set; }
        public int ListView { get; set; }
        public int ListSort { get; set; }
        public int TotalCount { get; set; }
        public IEnumerable<SelectListItem> SelectedCategories { get; set; }
        public IEnumerable<SelectListItem> Categories { get; set; }
        public bool NoMunicipalities { get; set; }
        public List<SelectListItem> Organizations { get; set; }
        public IEnumerable<SelectListItem> Ideas { get; set; }
        public IEnumerable<SelectListItem> Municipalities { get; set; }
        public string ProblemId { get; set; }

        public string OrganizationId { get; set; }
        public string OrganizationName { get; set; }
        public bool IsEditable { get; set; }

        public ProblemIndexModel()
        {
        }
    }
}