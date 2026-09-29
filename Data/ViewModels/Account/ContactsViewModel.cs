using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using Data.ViewModels.Base;
using Data.ViewModels.Organization;
using Framework.Infrastructure.Storage;

namespace Data.ViewModels.Account
{
    public class ContactsViewModel
    {
        public string UserObjectId { get; set; }
        //public string Country { get; set; }
        public string CurrentLocation { get; set; }
        public string OriginLocation { get; set; }
        public List<EmailModel> Emails { get; set; }
        public List<UrlViewModel> WebSites { get; set; }
        public List<PhoneNumberViewModel> PhoneNumbers { get; set; }
        public bool IsCurrentUser { get; set; }
        public bool IsFilled { get
        {
            return !string.IsNullOrEmpty(CurrentLocation) || !string.IsNullOrEmpty(OriginLocation) ||
                   WebSites.Count > 0 || PhoneNumbers.Count > 0;
        } }
    }

    public class EmailModel : EditableListModel
    {
        [DataType(DataType.EmailAddress)]
        [Required]
        [RegularExpression(@"[a-z0-9!#$%&'*+/=?^_`{|}~-]+(?:\.[a-z0-9!#$%&'*+/=?^_`{|}~-]+)*@(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]*[a-z0-9])?")]
        [Remote("ValidateEmail", "Account")]
        public string Email { get; set; }
        public bool IsConfirmed { get; set; }
        public bool SendMail { get; set; }
        public bool IsPrivate { get; set; }

        public EmailModel()
        {
        }
    }
}
