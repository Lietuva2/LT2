using System.Collections.Specialized;
using Data.ViewModels.Sponsor;

namespace Data.ViewModels.Account
{
    public class BankLinkModel
    {
        public class BankContacts
        {
            public string PhoneNumber { get; set; }
            public string Email { get; set; }
            public string Name { get; set; }
            public DonateModel Donate { get; set; }
            public bool CanConfirmByDonate { get; set; }
        }
        public string Target { get; set; }
        public NameValueCollection Values { get; set; }
        public BankContacts Contacts { get; set; }


        public BankLinkModel()
        {
            Values = new NameValueCollection();
        }
    }
}