using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using Framework.Mvc.DataAnnotations;
using Framework.Strings;

namespace Data.ViewModels.Account
{
    public class AdditionalUniqueInfoModel
    {
        [Required]
        public string AddressLine { get; set; }
        [RequiredIf("DocumentNoRequired")]
        public string DocumentNo { get; set; }
        public string VoteUrl { get; set; }
        public bool AllowSaveForNextUse { get; set; }
        public bool AcceptTerms { get; set; }
        public bool VotesArePublic { get; set; }
        public string OfficialUrl { get; set; }
        public bool AdditionalInfoRequired
        {
            get
            {
                return string.IsNullOrEmpty(AddressLine) ||
                    (DocumentNoRequired && string.IsNullOrEmpty(DocumentNo))
                    || !CityId.HasValue;
            }
        }
        [Required]
        public string City { get; set; }
        [Required]
        public string Municipality { get; set; }
        public int? MunicipalityId { get; set; }
        [Required]
        public string Country { get; set; }
        public int? CountryId { get; set; }
        public int? CityId { get; set; }
        [Required]
        public string FirstName { get; set; }
        [Required]
        public string LastName { get; set; }
        [Required]
        [RegularExpression(@"\d{11}")]
        public string PersonCode { get; set; }

        public bool DocumentNoRequired { get; set; }

        public string   Address
        {
            get
            {
                var address = AddressLine;
                if (!string.IsNullOrEmpty(City))
                {
                    address += ", " + City;
                }

                if (!string.IsNullOrEmpty(Country))
                {
                    address += ", " + Country;
                }

                return address;
            }
        }

        public AdditionalUniqueInfoModel()
        {
            AllowSaveForNextUse = false;
            DocumentNoRequired = true;
        }
    }
}
