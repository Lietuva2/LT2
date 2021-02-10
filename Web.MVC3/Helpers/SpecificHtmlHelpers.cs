using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Web.Mvc;
using System.Web.Mvc.Html;
using System.Web.Routing;
using System.Web.UI;
using Data.Enums;
using Data.ViewModels.Base;
using Data.ViewModels.NewsFeed;
using Framework.Mvc.Strings;
using Framework.Other;
using Framework.Strings;
using Globalization;
using Globalization.Resources.Services;
using Globalization.Resources.NewsFeed;
using Globalization.Resources.Shared;
using Services.Enums;
using InitiativeTypes = Data.Enums.InitiativeTypes;

namespace Web.Helpers
{
    /// <summary>
    /// Various project specific HTML helpers.
    /// </summary>
    public static class SpecificHtmlHelpers
    {
        public const int TextLengthInQueryString = 100;

        /// <summary>
        /// Renders success or failure message.
        /// </summary>
        /// <param name="htmlHelper">The HTML helper.</param>
        public static void RenderMessage(this HtmlHelper htmlHelper)
        {
            htmlHelper.RenderPartial(MVC.Shared.Views._Message);
        }

        /// <summary>
        /// Gets the CSS class for the specified model of type <typeparamref name="TModel"/> in case of validation error.
        /// </summary>
        /// <typeparam name="TModel">The type of the model.</typeparam>
        /// <param name="htmlHelper">The HTML helper.</param>
        /// <param name="columnSelector">The column selector.</param>
        /// <returns>CSS class for the model in case of validation error.</returns>
        public static string GetValidationCssClassFor<TModel>(this HtmlHelper<TModel> htmlHelper, Expression<Func<TModel, object>> columnSelector)
        {
            var expression = columnSelector.Body;
            if (expression.NodeType == ExpressionType.Convert)
            {
                expression = ((UnaryExpression)expression).Operand;
            }
            var propertyName = ((MemberExpression)expression).Member.Name;

            return htmlHelper.ViewData.ModelState.IsValidField(propertyName) ? String.Empty : "error";
        }

        public static string GetVisibilityClassString(this HtmlHelper htmlHelper, bool isVisible)
        {
            return isVisible ? string.Empty : " hide";
        }

        public static MvcHtmlString GetNewsFeedEntry(this HtmlHelper html, NewsFeedItemModel item)
        {
            var user = string.Empty;
            if (item.NewsFeedTypeId == (int)NewsFeedTypes.UserActivity)
            {
                user = item.UserFullName.Substring(0, item.UserFullName.IndexOf(" "));
            }
            else if (item.NewsFeedTypeId == (int)NewsFeedTypes.OrganizationActivity)
            {
                user = html.ActionLinkWithReturnUrl(item.UserFullName,
                                                         MVC.Account.Details(item.UserObjectId,
                                                                             item.UserFullName.ToSeoUrl(), null), new Dictionary<string, object>() { { "target", "_blank" } }).ToString();
            }
            else
            {
                var isOrganization = !string.IsNullOrEmpty(item.OrganizationId) && !string.IsNullOrEmpty(item.OrganizationName) && !item.IsPrivate &&
                                    ((ActionTypes)item.ActionTypeId).In(ActionTypes.IdeaCreated,
                                                         ActionTypes.IdeaVersionAdded,
                                                         ActionTypes.IssueCreated, ActionTypes.ProblemCreated);
                user = GetGroupedUsers(html, isOrganization ? item.OrganizationName : item.UserFullName,
                                isOrganization ? item.OrganizationId : item.UserObjectId, item.Users,
                                item.ActionTypeId != (int)ActionTypes.OrganizationMemberAdded ? item.UserCount : null,
                                isOrganization, item);
            }

            string str = string.Format(item.ActionDescription, user, "{1}", "{2}", "{3}", "{4}", "{5}");
            if (item.IsRead == false)
            {
                str = string.Format("<b>{0}</b>", str);
            }

            if (!item.Link.IsNullOrEmpty())
            {
                str = string.Format(str, string.Empty,
                    string.Format("<a href='{0}' target='_blank'>{1}</a>", item.Link, item.Subject.GetSafeHtml()),
                    "{2}", "{3}", "{4}", "{5}");
            }
            else
            {
                str = string.Format(str, string.Empty,
                    item.Subject,
                    "{2}", "{3}", "{4}", "{5}");
            }

            if (!item.RelatedUserObjectId.IsNullOrEmpty())
            {
                if (item.ActionTypeId == (int)ActionTypes.OrganizationMemberAdded)
                {
                    str = string.Format(str, string.Empty, string.Empty, GetGroupedUsers(html, item.RelatedUserFullName, item.RelatedUserObjectId, item.RelatedUsers, item.UserCount, false, item), "{3}", "{4}", "{5}");
                }
                else if (item.RelatedUserObjectId.Trim() == "-1")
                {
                    str = string.Format(str, string.Empty, string.Empty,
                                        " " + Resource.ForTaking + " ", "{3}", "{4}", "{5}");
                }
                else if (item.RelatedUserObjectId == item.UserObjectId && ((ActionTypes)item.ActionTypeId).In(ActionTypes.ToDoAdded, ActionTypes.ToDoEdited, ActionTypes.ToDoFinished, ActionTypes.ToDoCommentAdded))
                {
                    str = string.Format(str, string.Empty, string.Empty,
                                        " " + Resource.ForHimself + " ", "{3}", "{4}", "{5}");
                }
                else
                {
                    if (!item.RelatedUserFullName.IsNullOrEmpty())
                    {
                        str = string.Format(str, string.Empty, string.Empty,
                                            html.ActionLinkWithReturnUrl(item.RelatedUserFullName,
                                                                         MVC.Account.Details(item.RelatedUserObjectId,
                                                                                             item.RelatedUserFullName.
                                                                                                  ToSeoUrl(), null),
                                                                         new Dictionary<string, object>()
                                                                             {
                                                                                 {"target", "_blank"}
                                                                             }), "{3}", "{4}", "{5}");
                    }
                    else
                    {
                        str = string.Format(str, string.Empty, string.Empty, string.Empty, "{3}", "{4}", "{5}");
                    }
                }
            }

            if (((ActionTypes)item.ActionTypeId).In(ActionTypes.ToDoAdded, ActionTypes.ToDoEdited, ActionTypes.OrganizationToDoAdded, ActionTypes.OrganizationToDoEdited))
            {
                str = string.Format(str, string.Empty, string.Empty,
                                        " " + Resource.ForTheTeam + " ", "{3}", "{4}", "{5}");
            }

            if (item.CategoryNames.Count > 0)
            {
                str = string.Format(str, string.Empty, string.Empty, string.Empty, item.CategoryNames.Concatenate(", "));
            }

            if (!item.RelatedLink.IsNullOrEmpty() && !item.RelatedSubject.IsNullOrEmpty())
            {
                str = string.Format(str, string.Empty, string.Empty, string.Empty, string.Empty, string.Format("<a href='{0}'>{1}</a>", item.RelatedLink, item.RelatedSubject), string.Empty);
            }

            if (!item.OrganizationId.IsNullOrEmpty() && !item.OrganizationName.IsNullOrEmpty())
            {
                str = string.Format(str, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, html.ActionLink(item.OrganizationName, MVC.Organization.Details(item.OrganizationId, item.OrganizationName.ToSeoUrl(), null, null, null, null)));
            }

            return MvcHtmlString.Create(str);
        }

        public static MvcHtmlString GetNewsFeedEntry(NewsFeedItemModel item)
        {
            var isOrganization = !string.IsNullOrEmpty(item.OrganizationId) && !string.IsNullOrEmpty(item.OrganizationName) && !item.IsPrivate &&
                                    ((ActionTypes)item.ActionTypeId).In(ActionTypes.IdeaCreated,
                                                         ActionTypes.IdeaVersionAdded,
                                                         ActionTypes.IssueCreated, ActionTypes.ProblemCreated);
            var user = isOrganization ? item.OrganizationName : item.UserFullName;

            string str = string.Format(item.ActionDescription, user, "{1}", "{2}", "{3}", "{4}", "{5}");
            if (item.IsRead == false)
            {
                str = string.Format("<b>{0}</b>", str);
            }

            if (!item.Link.IsNullOrEmpty())
            {
                str = string.Format(str, string.Empty,
                    string.Format("<a href='https://www.lietuva2.lt{0}'>{1}</a>", item.Link, item.Subject),
                    "{2}", "{3}", "{4}", "{5}");
            }
            else
            {
                str = string.Format(str, string.Empty,
                    item.Subject,
                    "{2}", "{3}", "{4}", "{5}");
            }

            if (!item.RelatedUserObjectId.IsNullOrEmpty())
            {
                if (item.RelatedUserObjectId == item.UserObjectId && ((ActionTypes)item.ActionTypeId).In(ActionTypes.ToDoAdded, ActionTypes.ToDoEdited, ActionTypes.ToDoFinished, ActionTypes.ToDoCommentAdded))
                {
                    str = string.Format(str, string.Empty, string.Empty,
                                        " " + Resource.ForHimself + " ", "{3}", "{4}", "{5}");
                }
                else
                {
                    str = string.Format(str, string.Empty, string.Empty,
                                        item.RelatedUserFullName, "{3}", "{4}", "{5}");
                }
            }

            if (((ActionTypes)item.ActionTypeId).In(ActionTypes.ToDoAdded, ActionTypes.ToDoEdited, ActionTypes.OrganizationToDoAdded, ActionTypes.OrganizationToDoEdited))
            {
                str = string.Format(str, string.Empty, string.Empty,
                                        " " + Resource.ForTheTeam + " ", "{3}", "{4}", "{5}");
            }

            if (item.CategoryNames.Count > 0)
            {
                str = string.Format(str, string.Empty, string.Empty, string.Empty, item.CategoryNames.Concatenate(", "));
            }

            if (!item.RelatedLink.IsNullOrEmpty() && !item.RelatedSubject.IsNullOrEmpty())
            {
                str = string.Format(str, string.Empty, string.Empty, string.Empty, string.Empty, string.Format("<a href='{0}'>{1}</a>", item.RelatedLink, item.RelatedSubject), string.Empty);
            }

            if (!item.OrganizationId.IsNullOrEmpty() && !item.OrganizationName.IsNullOrEmpty())
            {
                str = string.Format(str, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, item.OrganizationName);
            }

            return MvcHtmlString.Create(str);
        }

        private static string GetGroupedUsers(HtmlHelper html, string name, string objectId, List<UserLinkModel> explicitUsers, int? userCount, bool isOrganization, NewsFeedItemModel item)
        {
            string user;
            if (!userCount.HasValue || userCount == 0)
            {
                if (isOrganization)
                {
                    return html.ActionLinkWithReturnUrl(name, MVC.Organization.Details(objectId, name.ToSeoUrl(), null, null, null, null)).ToString();
                }
                else
                {
                    return html.ActionLinkWithReturnUrl(name, MVC.Account.Details(objectId, name.ToSeoUrl(), null)).ToString();
                }
            }
            else
            {
                if (explicitUsers != null && explicitUsers.Count > 0)
                {
                    user =
                        explicitUsers.Concatenate(
                            itm => html.ActionLinkWithReturnUrl(itm.FullName, MVC.Account.Details(itm.ObjectId, itm.FullName.ToSeoUrl(), null)).ToString(), ", ");

                    if (userCount > 0)
                    {
                        user += " " +
                                string.Format(Resource.AndSomeMore,
                                              GlobalizedSentences.GetUsersString(userCount.Value),
                                              GetNewsFeedGroupUsersLink(html, item));

                    }
                }
                else
                {
                    user = string.Format(Resource.NumberUsers,
                                              GlobalizedSentences.GetUsersString(userCount.Value),
                                              GetNewsFeedGroupUsersLink(html, item));
                }
            }

            return user;
        }

        private static string GetNewsFeedGroupUsersLink(HtmlHelper html, NewsFeedItemModel item)
        {
            var type = item.NewsFeedTypeId == (int)NewsFeedTypes.NewsFeed
                                           ? NewsFeedListViews.MyNews
                                           : NewsFeedListViews.AllNews;
            return new UrlHelper(html.ViewContext.RequestContext).Action(MVC.NewsFeed.GetNewsFeedGroupUsers(type, item.ActionTypeId, item.ObjectId, item.Date, item.RelatedObjectId, item.OrganizationId, item.RawText.LimitLength(TextLengthInQueryString, null), item.IsPrivate));
        }

        public static string GetIdeaStateString(this HtmlHelper html, IdeaStates state, InitiativeTypes? type)
        {
            return type.HasValue ?
                Globalization.Resources.Services.InitiativeTypes.ResourceManager.GetString(type.ToString()) :
                    IdeaStatesResource.ResourceManager.GetString(state.ToString());
        }

        public static string GetStateCssClass(this HtmlHelper html, Data.Enums.IdeaStates state)
        {
            switch (state)
            {
                case Data.Enums.IdeaStates.New:
                    return "new";
                case Data.Enums.IdeaStates.Implementation:
                    return "impl";
                case Data.Enums.IdeaStates.Realized:
                    return "realized";
                case Data.Enums.IdeaStates.Closed:
                    return "closed";
                case IdeaStates.Voting:
                    return "beingvoted";
                case IdeaStates.Rejected:
                    return "rejected";
                case IdeaStates.Resolved:
                    return "resolved";
                default: return string.Empty;
            }
        }

        public static string GetControllerNameByEntryType(this HtmlHelper html, EntryTypes type)
        {
            if (type == EntryTypes.Issue)
            {
                return "Voting";
            }
            if (type == EntryTypes.User)
            {
                return "Account";
            }

            return type.ToString();
        }

        public static string GetBackgroundCssClass(this HtmlHelper html, ForAgainst forAgainst)
        {
            if (forAgainst == ForAgainst.Neutral)
            {
                return string.Empty;
            }

            if (forAgainst == ForAgainst.For)
            {
                return "background-for";
            }

            if (forAgainst == ForAgainst.Against)
            {
                return "background-against";
            }

            if (forAgainst == ForAgainst.Suggest)
            {
                return "background-suggest";
            }

            return string.Empty;
        }

        public static string GetTextCssClass(this HtmlHelper html, ForAgainst forAgainst)
        {
            if (forAgainst == ForAgainst.Neutral)
            {
                return string.Empty;
            }

            if (forAgainst == ForAgainst.For)
            {
                return "text-for";
            }

            if (forAgainst == ForAgainst.Against)
            {
                return "text-against";
            }

            if (forAgainst == ForAgainst.Suggest)
            {
                return "text-suggest";
            }

            return string.Empty;
        }

        public static Pair GetExpandableTextPair(this HtmlHelper helper, string text, int length)
        {
            string firstPart = "";
            string secondPart = "";
            try
            {
                text = text.Trim('\n', '\r');
                if (text.StartsWith("<p"))
                {
                    var plainText = text.GetPlainText(length).Trim();
                    var lastIndex = plainText.LastIndexOf(" ");
                    var secondLastIndex = plainText.Substring(0, lastIndex).Trim().LastIndexOf(" ");
                    var lastWord = plainText.Substring(lastIndex);
                    var secondLastWords = plainText.Substring(secondLastIndex);

                    int index = -1;
                    if (text.IndexOf(secondLastWords) > 0)
                    {
                        index = text.IndexOf(secondLastWords) + secondLastWords.Length;
                    }
                    else if (text.IndexOf(lastWord) > 0)
                    {
                        index = text.IndexOf(lastWord) + lastWord.Length;
                    }

                    if (index != -1)
                    {
                        firstPart = text.Substring(0, index) + "</p>";
                        secondPart = "<p>" + text.Substring(index);
                    }
                    else
                    {
                        firstPart = text.Substring(
                            text.IndexOf(@">") + 1,
                            text.IndexOf(@"</p>") - text.IndexOf(@">") - 1);

                        if (text.Length > text.IndexOf(@"</p>") + 4)
                        {
                            secondPart = text.Substring(text.IndexOf("</p>") + 4);
                        }
                    }
                }
                else
                {
                    firstPart = text.Substring(0, length);
                    while ((firstPart.Count(c => c == '<') != firstPart.Count(c => c == '>') ||
                            firstPart.Split(new[] { "<a" }, StringSplitOptions.None).Length !=
                            firstPart.Split(new[] { "</a>" }, StringSplitOptions.None).Length)
                           && length + 1 < text.Length - 1)
                    {
                        firstPart = text.Substring(0, ++length);
                    }
                    secondPart = text.Substring(length);
                }
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return new Pair(text, null);
            }

            return new Pair(firstPart, secondPart);
        }

        public static MvcHtmlString GetUniqueUsersTip(this HtmlHelper helper, int uniqueUsers, int notUniqueUsers)
        {
            return MvcHtmlString.Create(uniqueUsers + " " + SharedStrings.Unique +
                                        (notUniqueUsers > 0 ? " + " + notUniqueUsers + " " + SharedStrings.NotUnique : ""));
        }
    }
}
