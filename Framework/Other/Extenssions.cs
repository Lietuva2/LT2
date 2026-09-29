using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Framework.Other
{
    public static class Extenssions
    {
        /// <summary>
        /// Determines weather the object is in the specified object collection.
        /// </summary>
        /// <param name="o">The object.</param>
        /// <param name="vals">The object value collection.</param>
        /// <returns>ShutUpStyleCop.</returns>
        public static bool In(this object o, params object[] vals)
        {
            foreach (var val in vals)
            {
                if (o.Equals(val))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool In<T>(this T o, params T[] vals)
        {
            foreach (var val in vals)
            {
                if (o.Equals(val))
                {
                    return true;
                }
            }
            return false;
        }

        public static DateTime Truncate(this DateTime date, long resolution = TimeSpan.TicksPerSecond)
        {
            return new DateTime(date.Ticks - (date.Ticks % resolution), date.Kind);
        }
    }
}
