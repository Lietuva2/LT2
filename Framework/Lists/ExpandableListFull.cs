using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Framework.Lists
{
    public class ExpandableListFull<T>
    {
        public int PageSize { get; set; }
        public bool HasMoreElements { get; set; }
        public IEnumerable<T> List { get; set; }
        public int PageIndex { get; set; }

        public ExpandableListFull(IEnumerable<T> list, int pageSize)
        {
            this.PageSize = pageSize;
            this.HasMoreElements = list.Count() > pageSize;
            this.List = list.Take(pageSize);
        }
    }
}
