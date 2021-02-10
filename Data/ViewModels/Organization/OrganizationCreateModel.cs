using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;
using Data.Enums;
using Data.MongoDB;
using Data.ViewModels.NewsFeed;
using Framework.Lists;

namespace Data.ViewModels.Organization
{
    public class OrganizationCreateModel
    {
        [Required]
        public string Name { get; set; }
        public List<SelectListItem> Types { get; set; }
        public OrganizationTypes Type { get; set; }

        public OrganizationCreateModel()
        {
        }
    }
}
