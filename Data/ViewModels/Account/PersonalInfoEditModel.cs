using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using Framework.Infrastructure.Storage;

namespace Data.ViewModels.Account
{
    public class PersonalInfoEditModel
    {
        public string UserObjectId { get; set; }
        [Required]
        public string FirstName { get; set; }
        [Required]
        public string LastName { get; set; }
        [Required]
        [Remote("ValidateUserName", "Account")]
        public string UserName { get; set; }
        public int? BirthYear { get; set; }
        public int? BirthMonth { get; set; }
        public int? BirthDay { get; set; }
        public string Nationality { get; set; }
        public string Citizenship { get; set; }
        public IEnumerable<SelectListItem> EmploymentStatuses { get; set; }
        public string EmploymentStatusName { get; set; }
        public IEnumerable<SelectListItem> MaritalStatuses { get; set; }
        public string MaritalStatusName { get; set; }
        public IEnumerable<SelectListItem> Years { get; set; }
        public IEnumerable<SelectListItem> Months { get; set; }
        public IEnumerable<SelectListItem> Days { get; set; }
    }
}
