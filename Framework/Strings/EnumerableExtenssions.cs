using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Framework.Strings
{
    public static class EnumerableExtenssions
    {
        /// <summary>
        /// Concats the text.
        /// </summary>
        /// <param name="stringList">The string list.</param>
        /// <param name="delimeter">The delimeter.</param>
        /// <returns>Concatenated string list.</returns>
        public static string Concatenate(IList<string> stringList, string delimeter)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string str in stringList)
            {
                if (sb.Length != 0)
                {
                    sb.Append(delimeter);
                }

                sb.Append(str);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Concatenates specified items.
        /// </summary>
        /// <typeparam name="T">Type of items to concatenate.</typeparam>
        /// <param name="items">Items to concatenate.</param>
        /// <param name="separator">The separator.</param>
        /// <returns>Concatenated string.</returns>
        public static string Concatenate<T>(this IEnumerable<T> items, string separator)
        {
            return items.Concatenate(item => item.ToString(), separator);
        }

        /// <summary>
        /// Concatenates specified items.
        /// </summary>
        /// <typeparam name="T">Type of items to concatenate.</typeparam>
        /// <param name="items">Items to concatenate.</param>
        /// <param name="func">Function to use for concatenation.</param>
        /// <param name="separator">The separator.</param>
        /// <returns>Concatenated string.</returns>
        public static string Concatenate<T>(this IEnumerable<T> items, Func<T, string> func, string separator)
        {
            var sb = new StringBuilder();

            foreach (var item in items)
            {
                if (item != null)
                {
                    sb.Append(func(item));
                    sb.Append(separator);
                }
            }

            return sb.Length > separator.Length ? sb.ToString().Substring(0, sb.Length - separator.Length) : String.Empty;
        }
    }
}
