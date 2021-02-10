using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Data.ViewModels.Sponsor
{
    public class BankAccountItemModel
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public string Operation { get; set; }
        public double? Expense { get; set; }
        public double? Income { get; set; }
        public decimal? ExpenseDecimal { get { return (decimal?)Expense; } set { Expense = (double?)value; } }
        public decimal? IncomeDecimal { get { return (decimal?)Income; } set { Income = (double?)value; } }
        public string UserFullName { get; set; }
        public string UserObjectId { get; set; }
        public string OrganizationId { get; set; }
        public string OrganizationName { get; set; }
    }
}
