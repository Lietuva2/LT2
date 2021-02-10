using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Framework.Infrastructure.Storage;
using Framework.Lists;

namespace Data.ViewModels.Voting
{
    public class VotingIndexModel
    {
        public ExpandableList<VotingIndexItemModel> Items { get; set; }
        public int ListView { get; set; }
        public int ListSort { get; set; }
        public IEnumerable<SelectListItem> SelectedCategories { get; set; }
        public int TotalCount { get; set; }
        public bool NoMunicipalities { get; set; }

        public string OrganizationId { get; set; }
        public string OrganizationName { get; set; }
        public bool IsEditable { get; set; }
    }
}