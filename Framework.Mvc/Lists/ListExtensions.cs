using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using System.Web.Routing;

namespace Framework.Mvc.Lists
{
    public static class ListExtensions
    {
        public static IEnumerable<T> ForEach<T>(this IEnumerable<T> collection, Action<T> action)
        {
            foreach (var item in collection) action(item);
            return collection;
        }

        public static string CheckBoxList(this HtmlHelper htmlHelper, string name, IEnumerable<SelectListItem> items)
        {
            return htmlHelper.CheckBoxList(name, items, null);
        }

        public static string CheckBoxList(this HtmlHelper htmlHelper, string name, IEnumerable<SelectListItem> items, object htmlAttributes)
        {
            return htmlHelper.CheckBoxList(name, items, new RouteValueDictionary(htmlAttributes));
        }

        public static string CheckBoxList(this HtmlHelper htmlHelper, string name, IEnumerable<SelectListItem> items, IDictionary<string, object> htmlAttributes)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw new ArgumentException("The argument must have a value", "name");
            }

            if (items == null)
            {
                throw new ArgumentNullException("items");
            }

            var i = 0;
            var sb = new StringBuilder();
            foreach (var value in items)
            {
                var id = name + "_" + i;

                var span = new TagBuilder("span");
                span.MergeAttribute("class", "checkboxlist-option");

                var label = new TagBuilder("label");
                label.MergeAttribute("for", id);
                label.SetInnerText(value.Text);

                var input = new TagBuilder("input");
                input.MergeAttribute("type", "checkbox");
                input.MergeAttribute("value", value.Value);
                input.MergeAttribute("name", name);
                input.MergeAttributes(htmlAttributes);
                if (value.Selected)
                {
                    input.MergeAttribute("checked", "checked");
                }
                input.MergeAttribute("id", id);
                input.AddCssClass("checkbox");

                span.InnerHtml += input.ToString(TagRenderMode.SelfClosing);
                span.InnerHtml += label.ToString(TagRenderMode.Normal);

                sb.Append(span.ToString());
                i++;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Converts provided set of items into the <see cref="SelectListItem"/>s.
        /// </summary>
        /// <param name="source">The source items to convert.</param>
        /// <returns>
        /// The list of <see cref="SelectListItem"/>s.
        /// </returns>
        public static IEnumerable<SelectListItem> ToSelectList(this IEnumerable<TextValue> source)
        {
            return source.ToSelectList(true);
        }

        /// <summary>
        /// Converts provided set of items into the <see cref="SelectListItem"/>s.
        /// </summary>
        /// <param name="source">The source items to convert.</param>
        /// <param name="includeEmpty">if set to <c>true</c> [include empty].</param>
        /// <returns>
        /// The list of <see cref="SelectListItem"/>s.
        /// </returns>
        public static IEnumerable<SelectListItem> ToSelectList(this IEnumerable<TextValue> source, bool includeEmpty)
        {
            return source.ToSelectList(textvalue => textvalue.Value, textvalue => textvalue.Text, includeEmpty);
        }

        /// <summary>
        /// Converts provided set of items into the <see cref="SelectListItem"/>s.
        /// </summary>
        /// <param name="source">The source items to convert.</param>
        /// <param name="includeEmpty">If set to <c>true</c>, includes empty item.</param>
        /// <param name="selectedValues">The list of selected values.</param>
        /// <returns>
        /// The list of <see cref="SelectListItem"/>s.
        /// </returns>
        public static IEnumerable<SelectListItem> ToSelectList<TValue>(this IEnumerable<TextValue> source, bool includeEmpty, IEnumerable<TValue> selectedValues)
        {
            var values = selectedValues != null ? selectedValues.Select(value => value.ToString()).ToArray() : null;
            return source.ToSelectList(textvalue => textvalue.Value, textvalue => textvalue.Text, includeEmpty, values);
        }

        /// <summary>
        /// Converts provided set of items into the <see cref="SelectListItem"/>s.
        /// </summary>
        /// <typeparam name="TSource">The type of the source to convert.</typeparam>
        /// <param name="source">The source items to convert.</param>
        /// <param name="valueSelector">The value selector.</param>
        /// <param name="textSelector">The text selector.</param>
        /// <param name="includeEmpty">If set to <c>true</c>, includes empty item.</param>
        /// <param name="selectedValues">The list of selected values.</param>
        /// <returns>
        /// The list of <see cref="SelectListItem"/>s.
        /// </returns>
        public static IEnumerable<SelectListItem> ToSelectList<TSource>(this IEnumerable<TSource> source, Func<TSource, string> valueSelector, Func<TSource, string> textSelector, bool includeEmpty, params string[] selectedValues)
        {
            if (includeEmpty)
            {
                yield return new SelectListItem();
            }

            foreach (var textValue in source)
            {
                yield return new SelectListItem
                {
                    Value = valueSelector(textValue),
                    Text = textSelector(textValue),
                    Selected = selectedValues != null ? selectedValues.Contains(valueSelector(textValue)) : false
                };
            }
        }

        public static IEnumerable<IEnumerable<T>> CartesianProduct<T>(this IEnumerable<IEnumerable<T>> sequences)
        {
            IEnumerable<IEnumerable<T>> emptyProduct = new[] { Enumerable.Empty<T>() };
            return sequences.Aggregate(
                emptyProduct,
                (accumulator, sequence) =>
                from accseq in accumulator
                from item in sequence
                select accseq.Concat(new[] { item }));
        }
    }
}
