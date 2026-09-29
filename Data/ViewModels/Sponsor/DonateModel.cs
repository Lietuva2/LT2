using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;

namespace Data.ViewModels.Sponsor
{
    public class DonateModel
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        [Required(ErrorMessage = "Įveskite el. pašto adresą")]
        public string Email { get; set; }
        public int Amount { get; set; }
        public string PaymentType { get; set; }
        public string PersonCode { get; set; }
        public bool IsPersonCodeRequired { get; set; }

        public DonateModel()
        {
        }
    }
}
