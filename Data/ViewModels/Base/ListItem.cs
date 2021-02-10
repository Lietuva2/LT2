using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Data.ViewModels.Base
{
    public class ListItem
    {
        public string Name { get; set; }
        public int DbId { get; set; }
        public string ObjectId { get; set; }
        public bool IsDeleted { get; set; }
        public bool IsDeletable { get; set; }
    }
}
