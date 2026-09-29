using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Data.ViewModels.Account
{
    public class PointsPerCategoryModel
    {
        public int? CategoryId { get; set; }
        public string CategoryName { get; set; }
        public int Points { get; set; }
    }
}
