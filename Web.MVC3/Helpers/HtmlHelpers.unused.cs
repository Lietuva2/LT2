using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Web;
using System.Web.Mvc;
using System.Web.Mvc.Html;
using System.Web.Routing;
using Framework.Enums;

namespace Web.Helpers.Unused
{
    public static class HtmlHelpers
    {
        public static string Image(this HtmlHelper helper, string fileName)
        {
            return Image(helper, fileName, "");
        }
        public static string Image(this HtmlHelper helper, string fileName, string attributes)
        {
            fileName = string.Format("{0}", fileName);
            return string.Format("<img src='{0}' '{1}' />", helper.AttributeEncode(fileName), helper.AttributeEncode(attributes));
        }

        /// <summary>
        /// Gets the display name for the specified model.
        /// </summary>
        /// <typeparam name="TModel">The type of the model.</typeparam>
        /// <param name="htmlHelper">The HTML helper.</param>
        /// <param name="columnSelector">The column selector.</param>
        /// <returns>The display name of the specified model.</returns>
        public static MvcHtmlString GetDisplayNameFor<TModel>(this HtmlHelper<TModel> htmlHelper, Expression<Func<TModel, object>> columnSelector)
        {
            var expression = columnSelector.Body;
            if (expression.NodeType == ExpressionType.Convert)
            {
                expression = ((UnaryExpression)expression).Operand;
            }
            var property = ((MemberExpression)expression).Member;
            var propertyName = property.Name;
            var attribute = property.GetCustomAttributes(false).OfType<DisplayNameAttribute>().FirstOrDefault();

            return MvcHtmlString.Create(attribute != null ? attribute.DisplayName : propertyName);
        }

        /// <summary>
        /// Gets the display name for the specified model.
        /// </summary>
        /// <typeparam name="TModel">The type of the model.</typeparam>
        /// <param name="htmlHelper">The HTML helper.</param>
        /// <param name="columnSelector">The column selector.</param>
        /// <returns>The display name of the specified model.</returns>
        public static MvcHtmlString GetDisplayNameFor<TModel>(this HtmlHelper<IEnumerable<TModel>> htmlHelper, Expression<Func<TModel, object>> columnSelector)
        {
            var expression = columnSelector.Body;
            if (expression.NodeType == ExpressionType.Convert)
            {
                expression = ((UnaryExpression)expression).Operand;
            }
            var property = ((MemberExpression)expression).Member;
            var propertyName = property.Name;
            var attribute = property.GetCustomAttributes(false).OfType<DisplayNameAttribute>().FirstOrDefault();

            return MvcHtmlString.Create(attribute != null ? attribute.DisplayName : propertyName);
        }

        /// <summary>
        /// Gets the display name for the specified model.
        /// </summary>
        /// <typeparam name="TModel">The type of the model.</typeparam>
        /// <param name="htmlHelper">The HTML helper.</param>
        /// <param name="columnSelector">The column selector.</param>
        /// <returns>The display name of the specified model.</returns>
        public static MvcHtmlString GetDisplayNameFor<TModel>(this HtmlHelper<IList<TModel>> htmlHelper, Expression<Func<TModel, object>> columnSelector)
        {
            var expression = columnSelector.Body;
            if (expression.NodeType == ExpressionType.Convert)
            {
                expression = ((UnaryExpression)expression).Operand;
            }
            var property = ((MemberExpression)expression).Member;
            var propertyName = property.Name;
            var attribute = property.GetCustomAttributes(false).OfType<DisplayNameAttribute>().FirstOrDefault();

            return MvcHtmlString.Create(attribute != null ? attribute.DisplayName : propertyName);
        }

        /// <summary>
        /// Generates image button HTML markup for the specified action.
        /// </summary>
        /// <param name="htmlHelper">The HTML helper.</param>
        /// <param name="result">Action result.</param>
        /// <param name="actionHint">The action hint.</param>
        /// <param name="imageUrl">The image URL.</param>
        /// <param name="cssClass">The CSS class.</param>
        /// <returns>HTML markup for the specified action.</returns>
        public static MvcHtmlString ImageButton(this HtmlHelper htmlHelper, ActionResult result, string id, string actionHint, string imageUrl, string cssClass)
        {
            var input = new TagBuilder("input");
            input.MergeAttribute("type", "image");
            input.MergeAttribute("src", imageUrl);
            input.MergeAttribute("alt", actionHint);
            input.MergeAttribute("title", actionHint);
            input.MergeAttribute("id", id);
            input.MergeAttribute("name", id);
            input.AddCssClass(cssClass);

            var form = new TagBuilder("form");
            form.MergeAttribute("method", HtmlHelper.GetFormMethodString(FormMethod.Post));
            form.MergeAttribute("action", htmlHelper.GetUrl(result));
            form.MergeAttribute("id", id);
            form.MergeAttribute("name", id);
            form.InnerHtml = input.ToString(TagRenderMode.SelfClosing) + htmlHelper.AntiForgeryToken();

            return MvcHtmlString.Create(form.ToString());
        }
    }
}
