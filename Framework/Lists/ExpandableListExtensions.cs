using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Framework.Lists
{
    public static class ExpandableListExtensions
    {
        public static IQueryable<T> GetExpandablePage<T>(this IQueryable<T> enumerable, int pageIndex, int pageSize)
        {
            return enumerable.Skip(pageIndex*pageSize).Take(pageSize + 1);
        }

        public static IEnumerable<T> GetExpandablePage<T>(this IEnumerable<T> enumerable, int pageIndex, int pageSize)
        {
            return enumerable.Skip(pageIndex * pageSize).Take(pageSize + 1);
        }
    }
}
