using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Linq;
using System.Linq.Expressions;
using System.Resources;
using System.Text;
using System.Web;
using System.Web.Mvc;
using System.Web.Mvc.Html;
using System.Web.Optimization;
using System.Web.Routing;
using Data.Enums;
using Framework.Enums;
using Globalization.Resources.Shared;
using Recaptcha;
using Services.Infrastructure;

namespace Web.Helpers
{
    public static class HtmlHelpers
    {
        public static MvcHtmlString EnableJavascriptGlobalization(UrlHelper helper)
        {
            var jsPath = string.Format("<script src='{0}' type='text/javascript'></script>\n", Links.Scripts.Globalization.Url(SharedStrings.JavascriptGlobalizationFile));
            return MvcHtmlString.Create(jsPath);
        }
        public static string ToAbsoluteUrl(this UrlHelper helper, string url)
        {
            if(url.StartsWith("http"))
            {
                return url;
            }

            if(!url.StartsWith(@"/"))
            {
                url = "/" + url;
            }
            var abs = VirtualPathUtility.ToAbsolute("~" + url);
            return HttpContext.Current.Request.Url.GetLeftPart(UriPartial.Authority) + url;
        }

        /// <summary>
        /// Generates image link HTML markup for the specified action.
        /// </summary>
        /// <param name="htmlHelper">The HTML helper.</param>
        /// <param name="url">The link URL.</param>
        /// <param name="actionHint">The action hint.</param>
        /// <param name="imageUrl">The image URL.</param>
        /// <param name="cssClass">The CSS class.</param>
        /// <returns>HTML markup for the specified action.</returns>
        public static MvcHtmlString ImageLink(this HtmlHelper htmlHelper, string url, string id, string actionHint, string imageUrl, string cssClass)
        {
            var img = new TagBuilder("img");
            img.MergeAttribute("alt", actionHint);
            img.MergeAttribute("src", imageUrl);

            var a = new TagBuilder("a");
            a.AddCssClass(cssClass);
            a.MergeAttribute("href", url);
            a.MergeAttribute("title", actionHint);
            a.MergeAttribute("id", id);
            a.MergeAttribute("name", id);
            a.InnerHtml = img.ToString(TagRenderMode.SelfClosing);

            return MvcHtmlString.Create(a.ToString());
        }

        /// <summary>
        /// Gets the URL for the specified action result.
        /// </summary>
        /// <param name="htmlHelper">The HTML helper.</param>
        /// <param name="result">The action result.</param>
        /// <returns>The URL for the specified action result.</returns>
        public static string GetUrl(this HtmlHelper htmlHelper, ActionResult result)
        {
            var url = UrlHelper.GenerateUrl(/*routeName*/ null,
                                            result.GetT4MVCResult().Action,
                                            result.GetT4MVCResult().Controller,
                                            result.GetT4MVCResult().RouteValueDictionary,
                                            htmlHelper.RouteCollection,
                                            htmlHelper.ViewContext.RequestContext,
                /* includeImplicitMvcValues */ true);

            return url;
        }

        /// <summary>
        /// Gets the URL for the specified action result.
        /// </summary>
        /// <param name="urlHelper">The URL helper.</param>
        /// <param name="result">The action result.</param>
        /// <param name="additionalRouteValues">The additional route values.</param>
        /// <returns>The URL for the specified action result.</returns>
        public static string Action(this UrlHelper urlHelper, ActionResult result, RouteValueDictionary additionalRouteValues)
        {
            var routeValues = additionalRouteValues;
            foreach(var val in result.GetT4MVCResult().RouteValueDictionary)
            {
                routeValues.Add(val.Key, val.Value);
            }
            var url = urlHelper.Action(result.GetT4MVCResult().Action,
                                       result.GetT4MVCResult().Controller,
                                       routeValues);

            return url;
        }

        public static string ActionWithReturnUrl(this UrlHelper urlHelper, ActionResult result)
        {
            var routeValues = result.GetT4MVCResult().RouteValueDictionary;
            //routeValues.Add("returnTo", urlHelper.Encode(urlHelper.RequestContext.HttpContext.Request.RawUrl));
            var url = urlHelper.Action(result.GetT4MVCResult().Action,
                                       result.GetT4MVCResult().Controller,
                                       routeValues);

            return url;
        }

        public static MvcHtmlString ActionLinkWithReturnUrl(this HtmlHelper htmlHelper, string linkText, ActionResult result, IDictionary<string, object> htmlAttributes)
        {
            var routeValues = result.GetT4MVCResult().RouteValueDictionary;
            //routeValues.Add("returnTo", htmlHelper.ViewContext.HttpContext.Server.UrlEncode(htmlHelper.ViewContext.HttpContext.Request.RawUrl));
            var link = htmlHelper.ActionLink(linkText, result.GetT4MVCResult().Action, result.GetT4MVCResult().Controller, routeValues, htmlAttributes);
            return link;
        }

        public static MvcHtmlString ActionLinkWithReturnUrl(this HtmlHelper htmlHelper, string linkText, ActionResult result)
        {
            var routeValues = result.GetT4MVCResult().RouteValueDictionary;
            //routeValues.Add("returnTo", htmlHelper.ViewContext.HttpContext.Server.UrlEncode(htmlHelper.ViewContext.HttpContext.Request.RawUrl));
            var link = htmlHelper.ActionLink(linkText, result.GetT4MVCResult().Action, result.GetT4MVCResult().Controller, routeValues, null);
            return link;
        }

        public static MvcHtmlString BackLink(this HtmlHelper htmlHelper, UrlHelper urlHelper, ActionResult defaultRedirect)
        {
            return BackLink(htmlHelper);
        }

        public static MvcHtmlString BackLink(this HtmlHelper htmlHelper, Uri returnUrl)
        {
            if(returnUrl == null)
            {
                return BackLink(htmlHelper);
            }

            return BackLink(htmlHelper, returnUrl.PathAndQuery, SharedStrings.Back);
        }

        public static MvcHtmlString BackLink(this HtmlHelper htmlHelper, string returnUrl, string text)
        {
            return MvcHtmlString.Create("<a href='"+returnUrl+"'>" + text + "</a>");
        }

        public static MvcHtmlString BackLink(this HtmlHelper htmlHelper)
        {
            return MvcHtmlString.Create("<a href='javascript:goBack();'>" + SharedStrings.Back + "</a>");
        }

        public static MvcHtmlString AutoCompleteFor<TModel>(this HtmlHelper<TModel> htmlHelper, Expression<Func<TModel, object>> columnSelector, ActionResult result)
        {
            return AutoCompleteFor(htmlHelper, columnSelector, result, null);
        }

        public static MvcHtmlString AutoCompleteFor<TModel>(this HtmlHelper<TModel> htmlHelper, Expression<Func<TModel, object>> columnSelector, ActionResult result, Dictionary<string, string> parentControlSelectors)
        {
            return AutoCompleteFor(htmlHelper, columnSelector, result, parentControlSelectors, true);
        }

        public static MvcHtmlString AutoCompleteFor<TModel>(this HtmlHelper<TModel> htmlHelper, Expression<Func<TModel, object>> columnSelector, ActionResult result, Dictionary<string, string> parentControlSelectors, bool mustMatch)
        {
            return AutoCompleteFor(htmlHelper, columnSelector, result, parentControlSelectors, mustMatch, string.Empty);
        }

        public static MvcHtmlString AutoCompleteFor<TModel>(this HtmlHelper<TModel> htmlHelper, Expression<Func<TModel, object>> columnSelector, ActionResult result, Dictionary<string, string> parentControlSelectors, bool mustMatch, string cssClassName)
        {
            var className = "autocomplete_" + FieldIdFor(htmlHelper, columnSelector);
            var textBox = htmlHelper.TextBoxFor(columnSelector, new Dictionary<string, object> { { "class", className + (!string.IsNullOrEmpty(cssClassName) ? " " + cssClassName : string.Empty) } }).ToString();
            string selectors = GetSelectors(parentControlSelectors);
            var script = string.Format("$(document).ready(function(){{initAutocomplete($('.{0}'), '{1}', {2}, '{3}', {4});}});",
                                        className,
                                        GetUrl(htmlHelper, result),
                                        string.IsNullOrEmpty(selectors) ? "null" : selectors,
                                        "prefix",
                                        mustMatch.ToString().ToLower());
            var fullScript = string.Format("<script type='text/javascript'>{0}</script>", script);
            var html = textBox + fullScript;
            return MvcHtmlString.Create(html);
        }

        //public static MvcHtmlString AutoCompleteFor<TModel>(this HtmlHelper<TModel> htmlHelper, Expression<Func<TModel, object>> columnSelector, Expression<Func<TModel, object>> idColumnSelector, ActionResult result)
        //{
        //    return AutoCompleteFor(htmlHelper, columnSelector, idColumnSelector, result, string.Empty);
        //}

        //public static MvcHtmlString AutoCompleteFor<TModel>(this HtmlHelper<TModel> htmlHelper, Expression<Func<TModel, object>> columnSelector, Expression<Func<TModel, object>> idColumnSelector, ActionResult result, string cssClassName)
        //{
        //    return AutoCompleteFor(htmlHelper, columnSelector, idColumnSelector, result, cssClassName, null);
        //}

        //public static MvcHtmlString AutoCompleteFor<TModel>(this HtmlHelper<TModel> htmlHelper, Expression<Func<TModel, object>> columnSelector, Expression<Func<TModel, object>> idColumnSelector, ActionResult result, string cssClassName, string itemSelectCallBack)
        //{
        //    var className = "autocomplete_" + FieldIdFor(htmlHelper, columnSelector);
        //    var textBox = htmlHelper.TextBoxFor(columnSelector, new Dictionary<string, object> { { "class", className + (!string.IsNullOrEmpty(cssClassName) ? " " + cssClassName : string.Empty) } }).ToString();
        //    var hiddenId = htmlHelper.HiddenFor(idColumnSelector);
        //    var script = string.Format("$(document).ready(function(){{initAutocomplete($('.{0}'), '{1}', {2}, '{3}', {4}, $('#{5}'), {6});}});",
        //                                className,
        //                                GetUrl(htmlHelper, result),
        //                                "null",
        //                                "prefix",
        //                                "true",
        //                                 FieldIdFor(htmlHelper, idColumnSelector),
        //                                 itemSelectCallBack ?? "null");
        //    var fullScript = string.Format("<script type='text/javascript'>{0}</script>", script);
        //    var html = textBox + hiddenId + fullScript;
        //    return MvcHtmlString.Create(html);
        //    //return AutoCompleteFor(htmlHelper, FieldIdFor(htmlHelper, columnSelector).ToString(),
        //    //                       FieldIdFor(htmlHelper, idColumnSelector).ToString(), result, cssClassName, itemSelectCallBack);
        //}

        public static MvcHtmlString AutoComplete<TModel>(this HtmlHelper<TModel> htmlHelper, string textBoxId, string hiddenValueId, ActionResult result, string cssClassName = null, string itemSelectCallBack = null, bool mustMatch = true, Dictionary<string, object> attributes = null, string text = null, string val = null)
        {
            var className = "autocomplete_" + textBoxId;
            if(attributes == null)
            {
                attributes = new Dictionary<string, object>();
            }

            attributes.Add("class", className + (!string.IsNullOrEmpty(cssClassName) ? " " + cssClassName : string.Empty));

            var textBox = htmlHelper.TextBox(textBoxId, text, attributes).ToString();
            var hiddenId = htmlHelper.Hidden(hiddenValueId, val);
            var script = string.Format("$(document).ready(function(){{initAutocomplete($('.{0}'), '{1}', {2}, '{3}', {4}, '#{5}', {6});}});",
                                        className,
                                        GetUrl(htmlHelper, result),
                                        "null",
                                        "prefix",
                                        mustMatch ? "true" : "false",
                                         hiddenValueId,
                                         itemSelectCallBack ?? "null");
            var fullScript = string.Format("<script type='text/javascript'>{0}</script>", script);
            var html = textBox + hiddenId + fullScript;
            return MvcHtmlString.Create(html);
        }

        public static MvcHtmlString FieldIdFor<TModel, TValue>(this HtmlHelper<TModel> html,
            Expression<Func<TModel, TValue>> expression)
        {
            string htmlFieldName = ExpressionHelper.GetExpressionText(expression);
            string inputFieldId = html.ViewContext.ViewData.TemplateInfo.GetFullHtmlFieldId(htmlFieldName);
            return MvcHtmlString.Create(inputFieldId);
        }

        public static MvcHtmlString FieldNameFor<TModel, TValue>(this HtmlHelper<TModel> html,
            Expression<Func<TModel, TValue>> expression)
        {
            string htmlFieldName = ExpressionHelper.GetExpressionText(expression);
            string inputFieldId = html.ViewContext.ViewData.TemplateInfo.GetFullHtmlFieldName(htmlFieldName);
            return MvcHtmlString.Create(inputFieldId);
        }

        public static MvcHtmlString GetDecisionClass(this HtmlHelper helper, ForAgainst decision)
        {
            return MvcHtmlString.Create(decision == ForAgainst.For ? "progessbar-content green-border" : decision == ForAgainst.Against ? "progessbar-content red-border" : "progessbar-content-transparent");
        }

        private static string GetSelectors(Dictionary<string, string> parentControlSelectors)
        {
            return SerializeToJson(parentControlSelectors, "{0}: function(){{return {1}; }}");
        }

        private static string SerializeToJson(Dictionary<string, string> dict, string scriptTemplate)
        {
            if (dict == null)
            {
                return null;
            }

            string script = "{";
            foreach (var item in dict)
            {
                script += string.Format(scriptTemplate, item.Key, item.Value) + ",";
            }
            script = script.TrimEnd(',');
            script += "}";
            return script;
        }

        public static IHtmlString RegisterCommonScripts(this HtmlHelper helper)
        {
            return Scripts.Render("~/site/js");
        }

        public static IHtmlString RegisterExtraScripts(this HtmlHelper helper)
        {
            return Scripts.Render("~/extra/js");
        }

        public static IHtmlString RegisterCkeditorScripts(this HtmlHelper helper)
        {
            var url = new UrlHelper(helper.ViewContext.RequestContext);
            var result = helper.Script(url.Content("~/ckeditor/ckeditor.js")).ToString();
            result += helper.Script(url.Content("~/ckeditor/adapters/jquery.ckeditor.js")).ToString();
            return MvcHtmlString.Create(result);
        }

        public static IHtmlString RegisterCommonCss(this HtmlHelper helper)
        {
            return Styles.Render("~/Content/CSS/css");
        }

        public static MvcHtmlString Script(this HtmlHelper helper, string fileName, string version)
        {
            var v = Convert.ToInt32(version) + CustomAppSettings.Version;

            if (!fileName.EndsWith(".js"))
                fileName += ".js";

            fileName += "?v=" + v;
            var jsPath = string.Format("<script src='{0}' ></script>\n", helper.AttributeEncode(fileName));
            return MvcHtmlString.Create(jsPath);
        }

        public static MvcHtmlString Script(this HtmlHelper helper, string fileName)
        {
            return Script(helper, fileName, "0");
        }

        public static MvcHtmlString CSS(this HtmlHelper helper, string fileName)
        {
            return CSS(helper, fileName, "screen", "0");
        }

        public static MvcHtmlString CSS(this HtmlHelper helper, string fileName, string version)
        {
            var v = Convert.ToInt32(version) + CustomAppSettings.Version;
            return CSS(helper, fileName, "screen", "0");
        }

        public static MvcHtmlString CSS(this HtmlHelper helper, string fileName, string media, string version)
        {
            if (!fileName.EndsWith(".css"))
                fileName += ".css";
            //if (MvcApplication.Environment == AppEnvironment.Production && !fileName.EndsWith(".min.css"))
            //{
            //    fileName = fileName.Replace(".css", ".min.css");
            //}
            var v = Convert.ToInt32(version) + CustomAppSettings.Version;
            fileName += "?v=" + v;

            var jsPath = string.Format("<link rel='stylesheet' type='text/css' href='{0}'  media='" + media + "'/>\n", helper.AttributeEncode(fileName));
            return MvcHtmlString.Create(jsPath);
        }

        public static MvcHtmlString GenerateCaptcha(this HtmlHelper helper)
        {
            return MvcHtmlString.Create(helper.GenerateCaptcha("recaptcha", "default"));
        }

        public static MvcHtmlString DropDownForEnum<TModel, T>(this HtmlHelper<TModel> h, Expression<Func<TModel, T>> columnSelector, ResourceManager rm, string emptyText = "")
        {
            Type enumType = typeof(T);
            var isNullable = false;
            if (enumType.IsGenericType && enumType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                // We are dealing with a generic type that is nullable
                enumType = Nullable.GetUnderlyingType(enumType);
                isNullable = true;
            }

            var metadata = ModelMetadata.FromLambdaExpression(columnSelector, h.ViewData);
            var selectedValue = metadata.Model;
            TagBuilder t = new TagBuilder("select");
            t.MergeAttribute("name", FieldNameFor(h, columnSelector).ToString());
            t.MergeAttribute("id", FieldIdFor(h, columnSelector).ToString());
            if(isNullable)
            {
                TagBuilder option = new TagBuilder("option");
                option.MergeAttribute("value","");
                option.SetInnerText(emptyText);
                if(selectedValue == null)
                {
                    option.MergeAttribute("selected", "selected");
                }
                t.InnerHtml += option.ToString();
            }
            foreach (T val in Enum.GetValues(enumType))
            {
                string enumText = rm.GetString(val.ToString());
                if (String.IsNullOrEmpty(enumText)) enumText = val.ToString();
                TagBuilder option = new TagBuilder("option");
                option.MergeAttribute("value", (val).ToString());
                if (val.Equals(selectedValue))
                {
                    option.MergeAttribute("selected", "selected");
                }
                option.SetInnerText(enumText);
                t.InnerHtml += option.ToString();
            }

            return MvcHtmlString.Create(t.ToString());
        }

        public static MvcHtmlString RadioButtonListForEnum<TModel, T>(this HtmlHelper<TModel> helper, Expression<Func<TModel, T>> columnSelector, ResourceManager rm, IDictionary<string, object> htmlAttributes = null)
        {
            Type enumType = typeof(T);
            var id = FieldIdFor(helper, columnSelector).ToString();

            string html = string.Empty;

            int i = 0;
            foreach (Enum val in Enum.GetValues(enumType))
            {
                var dict = new Dictionary<string, object>()
                                                      {
                                                          {"id", id + (i++)}
                                                      };
                if (i == 1)
                {
                    dict.Add("checked", "checked");
                }

                if (htmlAttributes != null)
                {
                    foreach (var attr in htmlAttributes)
                    {
                        dict.Add(attr.Key, attr.Value);
                    }
                }

                var label = new TagBuilder("label");
                label.MergeAttribute("class", "radio");

                string innerhtml = helper.RadioButtonFor(columnSelector, val, dict).ToString();
                string enumText = rm.GetString(val.ToString());
                if (String.IsNullOrEmpty(enumText)) enumText = val.ToString();
                innerhtml += enumText;
                label.InnerHtml = innerhtml;
                html += label.ToString(TagRenderMode.Normal);
            }

            return MvcHtmlString.Create(html);
        }

        public static MvcHtmlString Buttons(this HtmlHelper helper, int buttonCount = 1, string backUrl = "javascript:history.back()", string saveText = null, string id = null)
        {
            if (string.IsNullOrEmpty(saveText))
            {
                saveText = SharedStrings.Save;
            }

            var buttons = new List<ButtonHelperModel>();
            buttons.Add(new ButtonHelperModel()
                            {
                                CssClass = "positive",
                                Text = saveText,
                                Type = ButtonHelperModel.Types.Button,
                                ImageUrl = Links.Content.Images.tick_png,
                                Id = id
                            });
            if(buttonCount == 2)
            {
                buttons.Add(new ButtonHelperModel()
                {
                    CssClass = "negative cancel",
                    Text = SharedStrings.Cancel,
                    Type = ButtonHelperModel.Types.Link,
                    ImageUrl = Links.Content.Images.cross_png,
                    Href = backUrl
                });
            }

            return Buttons(helper, buttons);
        }

        public static MvcHtmlString Button(this HtmlHelper helper, ButtonHelperModel button, string containerCssClass = "buttons")
        {
            return Buttons(helper, new List<ButtonHelperModel>() {button}, containerCssClass);
        }

        public static MvcHtmlString Buttons(this HtmlHelper helper, params ButtonHelperModel[] buttons)
        {
            return Buttons(helper, buttons.ToList());
        }

        public static MvcHtmlString Buttons(this HtmlHelper helper, List<ButtonHelperModel> buttons, string containerCssClass = "buttons")
        {
            var container = new TagBuilder("div");
            container.AddCssClass(containerCssClass);
            
            foreach(var button in buttons)
            {
                if(button.Type == ButtonHelperModel.Types.Button)
                {
                    var buttonTag = new TagBuilder("button");
                    buttonTag.MergeAttribute("type", "submit");
                    buttonTag = GetButtonContents(buttonTag, button);
                    container.InnerHtml += buttonTag.ToString();
                }
                else
                {
                    var linkTag = new TagBuilder("a");
                    if (!button.Disabled)
                    {
                        linkTag.MergeAttribute("href", button.Href);
                    }
                    linkTag = GetButtonContents(linkTag, button);
                    container.InnerHtml += linkTag.ToString();
                }
            }

            //var clear = new TagBuilder("div");
            //clear.AddCssClass("clear");
            var html = container.ToString();
            //html += clear.ToString();

            return MvcHtmlString.Create(html);
        }

        private static TagBuilder GetButtonContents(TagBuilder tag, ButtonHelperModel button)
        {
            if (!string.IsNullOrEmpty(button.CssClass))
            {
                tag.AddCssClass(button.CssClass);
            }

            if(!string.IsNullOrEmpty(button.Id))
            {
                tag.MergeAttribute("id", button.Id); 
            }

            if (!string.IsNullOrEmpty(button.Name))
            {
                tag.MergeAttribute("name", button.Name);
            }

            if(!string.IsNullOrEmpty(button.Value))
            {
                tag.MergeAttribute("value", button.Value);
            }

            if(button.Disabled)
            {
                tag.MergeAttribute("disabled", "1");
                tag.AddCssClass("disabled");
            }

            if (button.HtmlAttributes != null)
            {
                foreach (var attr in button.HtmlAttributes)
                {
                    tag.MergeAttribute(attr.Key, attr.Value.ToString());
                }
            }
            
            if (!string.IsNullOrEmpty(button.ImageUrl))
            {
                var img = new TagBuilder("img");
                img.MergeAttribute("src", button.ImageUrl);
                img.MergeAttribute("alt", "");
                tag.InnerHtml = img.ToString(TagRenderMode.SelfClosing);
            }

            tag.InnerHtml += button.Text ?? SharedStrings.Save;
            return tag;
        }
    }



    public class ButtonHelperModel
    {
        public enum Types
        {
            Button,
            Link
        }

        public string Text { get; set; }
        public string CssClass { get; set; }
        public Types Type { get; set; }
        public string ImageUrl { get; set; }
        public string Href { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public string Value { get; set; }
        public bool Disabled { get; set; }
        public IDictionary<string, object> HtmlAttributes { get; set; }

        public ButtonHelperModel()
        {
            ImageUrl = Links.Content.Images.tick_png;
            Type = Types.Button;
            Href = "javascript:void(0);";
        }
    }

    public class CancelButtonHelperModel : ButtonHelperModel
    {
        public CancelButtonHelperModel()
        {
            Type = ButtonHelperModel.Types.Link;
            Id = "lnkCancel";
            Href = "javascript:void(0);";
            ImageUrl = Links.Content.Images.cross_png;
            Text = SharedStrings.Cancel;
        }
    }
}
