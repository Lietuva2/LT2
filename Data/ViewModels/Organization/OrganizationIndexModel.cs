using System.Collections.Generic;
using System.Web.Mvc;
using Data.Enums;
using Data.MongoDB;
using Data.ViewModels.NewsFeed;
using Framework.Lists;

namespace Data.ViewModels.Organization
{
    public class OrganizationIndexModel
    {
        public List<OrganizationIndexItemModel> Items { get; set; }

        public OrganizationIndexModel()
        {
        }
    }
}
