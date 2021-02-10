using System.Collections.Generic;
using Data.ViewModels.Base;

namespace Data.ViewModels.Organization
{
    public class ContactsViewModel
    {
        public string ObjectId { get; set; }
        public string Email { get; set; }
        public List<UrlViewModel> WebSites { get; set; }
        public string PhoneNumber { get; set; }
        public string Address { get; set; }

        public bool IsFilled { get; set; }
        public bool IsEditable { get; set; }
    }
}
