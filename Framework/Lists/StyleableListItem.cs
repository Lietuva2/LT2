using System;
using System.Linq;

namespace Framework.Lists
{
    /// <summary>
    /// Represents CSS styleable list item.
    /// </summary>
    /// <typeparam name="T">The type of list item.</typeparam>
    public class StyleableListItem<T>
    {
        /// <summary>
        /// Gets the index of the list item.
        /// </summary>
        /// <value>The index of the list item.</value>
        public int Index { get; private set; }

        /// <summary>
        /// Gets the list item.
        /// </summary>
        /// <value>The list item.</value>
        public T Item { get; private set; }

        /// <summary>
        /// Gets the position of the list item.
        /// </summary>
        /// <value>The position of the list item.</value>
        public ListItemPosition Position { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="StyleableListItem&lt;T&gt;"/> class.
        /// </summary>
        /// <param name="index">The index of the list item.</param>
        /// <param name="item">The list item.</param>
        /// <param name="position">The position of the list item.</param>
        public StyleableListItem(int index, T item, ListItemPosition position)
        {
            Index = index;
            Item = item;
            Position = position;
        }

        /// <summary>
        /// Determines whether current list item has specified description.
        /// </summary>
        /// <param name="position">The list item position.</param>
        /// <returns><c>True</c> if current list item has specified description; otherwise, <c>false</c>.</returns>
        public bool Is(ListItemPosition position)
        {
            return (Position & position) == position;
        }

        /// <summary>
        /// Gets the CSS class based on the current list item description.
        /// </summary>
        /// <param name="prefix">The prefix to use with the CSS class.</param>
        /// <returns>The CSS class.</returns>
        public string GetCssClass(string prefix)
        {
            if (prefix == null)
            {
                throw new ArgumentNullException("prefix");
            }

            var matchingDescriptions = Enum.GetValues(typeof(ListItemPosition)).Cast<ListItemPosition>().Where(Is);
            var cssClass = String.Join(" ", matchingDescriptions.Select(description => prefix + description.ToString().ToLower()).ToArray());

            return cssClass;
        }

        /// <summary>
        /// Gets the CSS class based on the current list item description.
        /// </summary>
        /// <returns>The CSS class.</returns>
        public string GetCssClass()
        {
            return GetCssClass(String.Empty);
        }

        /// <summary>
        /// Performs an implicit conversion from <see cref="StyleableListItem{T}"/> to <see cref="T"/>.
        /// </summary>
        /// <param name="item">The styleable list item.</param>
        /// <returns>The result of the conversion.</returns>
        public static implicit operator T(StyleableListItem<T> item)
        {
            return item.Item;
        }
    }
}