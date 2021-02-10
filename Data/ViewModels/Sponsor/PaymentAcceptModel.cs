using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Data.ViewModels.Sponsor
{
    public class PaymentAcceptModel
    {
        public bool Success { get; set; }
        public int? PersonCodeStatus { get; set; }

        public PaymentAcceptModel()
        {
        }
    }
}
