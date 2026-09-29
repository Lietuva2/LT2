using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Framework.Infrastructure.Storage;

namespace Data.ViewModels.Account
{
    public class PersonalInfoViewModel
    {
        public string UserObjectId { get; set; }
        public string FullName { get; set; }
        public string UserName { get; set; }
        public string BirthDate { get; set; }
        public string EmploymentStatusName { get; set; }
        public string MaritalStatusName { get; set; }
        public string Nationality { get; set; }
        public string Citizenship { get; set; }
        public bool IsCurrentUser { get; set; }
        public bool IsFilled { get
        {
            return !string.IsNullOrEmpty(BirthDate) ||
                   !string.IsNullOrEmpty(Nationality)
                   || !string.IsNullOrEmpty(EmploymentStatusName) ||
                   !string.IsNullOrEmpty(MaritalStatusName);
        } }
    }
}
