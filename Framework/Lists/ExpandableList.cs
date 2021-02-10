using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Framework.Lists
{
    public class ExpandableList<T>
    {
        public int PageSize { get; set; }
        public bool HasMoreElements { get; set; }
        public IEnumerable<T> List { get; set; }

        public ExpandableList(IEnumerable<T> list, int pageSize)
        {
            this.PageSize = pageSize;
            this.HasMoreElements = list.Count() > pageSize;
            this.List = list.Take(pageSize);
        }
    }
}
