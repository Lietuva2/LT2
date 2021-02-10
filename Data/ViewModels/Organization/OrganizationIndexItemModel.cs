using System.Collections.Generic;
using Data.Enums;
using Data.MongoDB;
using Data.ViewModels.NewsFeed;
using Framework.Lists;

namespace Data.ViewModels.Organization
{
    public class OrganizationIndexItemModel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public int MembersCount { get; set; }
        public bool HasPicture { get; set; }
        public ContactsViewModel Contacts { get; set; }
        public InfoViewModel Info { get; set; }

        public OrganizationIndexItemModel()
        {
            Contacts = new ContactsViewModel();
            Info = new InfoViewModel();
        }
    }
}
