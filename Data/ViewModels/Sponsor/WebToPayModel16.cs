using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Data.ViewModels.Sponsor
{
    public class WebToPayModel
    {
        public string firstname { get; set; }
        public string lastname { get; set; }
        public string email { get; set; }
        public string personcode { get; set; }
        public string orderid { get; set; }
        public string data { get; set; }
        public string sign { get; set; }
        public string amount { get; set; }
        public string AmountLTL { get { return (Convert.ToDecimal(amount) / 100).ToString(); } }
        public string PaymentType { get; set; }
        public List<GiftModel> Gifts { get; set; }


        public WebToPayModel()
        {
        }
    }
}
