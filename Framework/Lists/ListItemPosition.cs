using System;

namespace Framework.Lists
{
    /// <summary>
    /// The item position inside list.
    /// </summary>
    [Flags]
    public enum ListItemPosition
    {
        /// <summary>
        /// Item is first.
        /// </summary>
        First = 1,

        /// <summary>
        /// Item is last.
        /// </summary>
        Last = 2,

        /// <summary>
        /// Item is nor first, neither last.
        /// </summary>
        Interior = 4,
        
        /// <summary>
        /// Item is even.
        /// </summary>
        Even = 8,
        
        /// <summary>
        /// Item is odd.
        /// </summary>
        Odd = 16,
    }
}