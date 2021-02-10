using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Data.ViewModels.Account;
using Data.ViewModels.Base;
using Framework.Mvc.DataAnnotations;

namespace Data.ViewModels.Organization
{
    public class ContactsEditModel
    {
        public string ObjectId { get; set; }

        [RegularExpression(@"[ \d-\(\)\+]+")]
        public string PhoneNumber { get; set; }
        public string Address { get; set; }

        [DataType(DataType.EmailAddress)]
        [RegularExpression(@"[a-z0-9!#$%&'*+/=?^_`{|}~-]+(?:\.[a-z0-9!#$%&'*+/=?^_`{|}~-]+)*@(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]*[a-z0-9])?")]
        public string Email { get; set; }

        public List<UrlEditModel> WebSites { get; set; }

        public ContactsEditModel()
        {
            WebSites = new List<UrlEditModel>();
        }
    }
}
