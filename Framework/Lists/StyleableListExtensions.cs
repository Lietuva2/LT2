using System.Collections.Generic;

namespace Framework.Lists
{
    /// <summary>
    /// The extension methods for the styleable list.
    /// </summary>
    public static class StyleableListExtensions
    {
        public static IEnumerable<StyleableListItem<T>> AsStyleableList<T>(this IEnumerable<T> items)
        {
            // to avoid evaluating the whole collection up-front (which may be undesirable, for example
            // if the collection contains infinitely many members), read-ahead just one item at a time.

            // get the first item
            var enumerator = items.GetEnumerator();
            if (!enumerator.MoveNext())
            {
                yield break;
            }

            T currentItem = enumerator.Current;
            var index = 0;

            while (true)
            {
                // read ahead so we know whether we're at the end or not
                var isLast = !enumerator.MoveNext();

                var position = (index % 2 == 0 ? ListItemPosition.Odd : ListItemPosition.Even);
                if (index == 0)
                {
                    position |= ListItemPosition.First;
                }
                if (isLast)
                {
                    position |= ListItemPosition.Last;
                }
                if (index > 0 && !isLast)
                {
                    position |= ListItemPosition.Interior;
                }
                yield return new StyleableListItem<T>(index, currentItem, position);

                // terminate or continue
                if (isLast)
                {
                    yield break;
                }

                index++;
                currentItem = enumerator.Current;
            }
        }
    }
}