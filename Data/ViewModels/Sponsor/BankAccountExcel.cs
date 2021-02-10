using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Data.ViewModels.Sponsor
{
    public class BankAccountModel
    {
        public BankAccountModel()
        {
            Accounts = new List<AccountModel>();
            WebToPayItems = new List<BankAccountItemModel>();
        }

        public IEnumerable<AccountModel> Accounts { get; set; }
        public IEnumerable<BankAccountItemModel> WebToPayItems { get; set; }

        public class AccountModel
        {
            public AccountModel()
            {
                Items = new List<BankAccountItemModel>();
            }

            public int Id { get; set; }
            public IEnumerable<BankAccountItemModel> Items { get; set; }
            
            public string AccountNo { get; set; }
            public double Balance { get; set; }
            public string Currency { get; set; }
        }
    }
}
