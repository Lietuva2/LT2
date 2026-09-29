using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;
using Data.MongoDB;
using Data.ViewModels.Base;
using Data.ViewModels.Organization;
using Framework.Mvc.DataAnnotations;
using Framework.Infrastructure.Storage;

namespace Data.ViewModels.Account
{
    public class ContactsEditModel
    {
        public string UserObjectId { get; set; }

        [RequiredIf("Municipality")]
        public string Country { get; set; }

        public int? CountryId { get; set; }
        
        public string City { get; set; }

        [RequiredIf("City")]
        public string Municipality { get; set; }

        public int? MunicipalityId { get; set; }

        public string OriginCity { get; set; }

        [RequiredIf("OriginCity")]
        public string OriginMunicipality { get; set; }

        public int? OriginMunicipalityId { get; set; }

        [RequiredIf("OriginMunicipality")]
        public string OriginCountry { get; set; }

        public int? OriginCountryId { get; set; }

        //[DataType(DataType.EmailAddress)]
        //[Required]
        //[RegularExpression(@"[a-z0-9!#$%&'*+/=?^_`{|}~-]+(?:\.[a-z0-9!#$%&'*+/=?^_`{|}~-]+)*@(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]*[a-z0-9])?")]
        //[Remote("ValidateEmail", "Account")]
        //public string Email { get; set; }
        [ListNotEmpty(ErrorMessage = "Įveskite el. paštą")]
        public List<EmailModel> Emails { get; set; }

        public List<PhoneNumberEditModel> PhoneNumbers { get; set; }

        public List<UrlEditModel> WebSites { get; set; }

        public int EditIndex { get; set; }

        public ContactsEditModel()
        {
            WebSites = new List<UrlEditModel>();
            PhoneNumbers = new List<PhoneNumberEditModel>();
            Emails = new List<EmailModel>();
        }
    }
}
