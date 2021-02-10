using System;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using System.Web.Security;
using Framework;
using Framework.Data;
using Framework.Exceptions;
using Framework.Mvc;
using Framework.Mvc.ActionResults;
using Framework.Mvc.Filters;
using Framework.Mvc.Mvc;
using Globalization.Resources.NewsFeed;
using Services.Infrastructure;
using Services.ModelServices;
using Services.Session;

namespace Web.Controllers
{
    [RequireSsl]
    public partial class SiteBaseController : BaseController
    {
        private const string TourMessageKey = "TourMessage";

        private const string TourLinkKey = "TourLink";
        
        /// <summary>
        /// Gets or sets the current user.
        /// </summary>
        /// <value>The current user.</value>
        public virtual UserInfo CurrentUser
        {
            get { return MembershipSession.GetUser(); }
        }

        public MunicipalityInfo Municipality
        {
            get { return WorkContext.Municipality; }
            set { WorkContext.Municipality = value; }
        }

        protected string OrganizationId
        {
            get { return Session["OrganizationId"] != null ? Session["OrganizationId"].ToString() : null; }
            set { Session["OrganizationId"] = value; }
        }

        protected bool IsGoogleAuthenticated
        {
            get
            {
                return Session["Google.DriveService"] != null;
            }
        }

        public bool IsTour
        {
            get
            {
                return (bool)(Session["tour"] ?? false);
            }
            set { Session["tour"] = value; }
        }

        public bool FirstTourPageVisited
        {
            get
            {
                return (bool)(Session["FirstTourPageVisited"] ?? false);
            }
            set { Session["FirstTourPageVisited"] = value; }
        }

        public bool SsoRedirect
        {
            get
            {
                return (bool)(Session["SsoRedirect"] ?? false);
            }
            set { Session["SsoRedirect"] = value; }
        }

        public string RedirectUrl
        {
            get
            {
                return (TempData["RedirectUrl"] ?? "").ToString();
            }
            set { TempData["RedirectUrl"] = value; }
        }

        protected Uri LastVoteUrl
        {
            get { return Session["lastVoteUrl"] as Uri; }
            set { Session["lastVoteUrl"] = value; }
        }

        protected Uri LastVoteReferrerUrl
        {
            get { return Session["lastVoteReferrerUrl"] as Uri; }
            set { Session["lastVoteReferrerUrl"] = value; }
        }

        protected Uri LastListUrl
        {
            get { return Session["LastListUrl"] as Uri; }
            set { Session["LastListUrl"] = value; }
        }

        protected int CurrentNumberOfPages
        {
            get
            {
                if (TempData["NumberOfPages"] == null)
                    //getBiggerPage is called only after the first page was already showed, so we need second at once.
                    TempData["NumberOfPages"] = 2;
                return (int)TempData["NumberOfPages"];
            }
            set { TempData["NumberOfPages"] = value; }
        }

        public virtual bool IsAuthenticated { get { return CurrentUser.IsAuthenticated; } }

        /// <summary>
        /// Redirects to the specified routing values and displays success message.
        /// </summary>
        /// <param name="routeValueDictionary">The route value dictionary.</param>
        /// <param name="message">The message.</param>
        /// <returns>Redirect action result.</returns>
        public virtual RedirectToRouteWithTempDataResult RedirectToSuccessAction(ActionResult result, string message, bool openDialog = false)
        {
            return RedirectToSuccessAction(result.GetRouteValueDictionary(), message, openDialog);
        }

        /// <summary>
        /// Redirects to the specified routing values and displays success message.
        /// </summary>
        /// <param name="routeValueDictionary">The route value dictionary.</param>
        /// <param name="message">The message.</param>
        /// <returns>Redirect action result.</returns>
        public virtual RedirectToRouteWithTempDataResult RedirectToSuccessAction(ActionResult result, bool openDialog = false)
        {
            return RedirectToSuccessAction(result.GetRouteValueDictionary(), openDialog);
        }

        /// <summary>
        /// Redirects to the specified routing values and displays failure message.
        /// </summary>
        /// <param name="routeValueDictionary">The route value dictionary.</param>
        /// <param name="message">The message.</param>
        /// <returns>Redirect action result.</returns>
        public virtual RedirectToRouteWithTempDataResult RedirectToFailureAction(ActionResult result, string message, bool openDialog = false)
        {
            return RedirectToFailureAction(result.GetRouteValueDictionary(), message, openDialog);
        }

        /// <summary>
        /// Performs save action and redirects to the specified routing values afterwards.
        /// </summary>
        /// <param name="save">The save action.</param>
        /// <param name="routeValueDictionary">The route value dictionary.</param>
        /// <returns>Redirect action result.</returns>
        public virtual RedirectToRouteWithTempDataResult SaveAndRedirect(Action save, ActionResult result)
        {
            return SaveAndRedirect(save, result.GetRouteValueDictionary());
        }

        /// <summary>
        /// Performs delete action and redirects to the specified routing values afterwards.
        /// </summary>
        /// <param name="delete">The delete action.</param>
        /// <param name="routeValueDictionary">The route value dictionary.</param>
        /// <returns>Redirect action result.</returns>
        public virtual RedirectToRouteWithTempDataResult DeleteAndRedirect(Action delete, ActionResult result)
        {
            return DeleteAndRedirect(delete, result.GetRouteValueDictionary());
        }

        public void StartTour()
        {
            IsTour = true;
            TempData[TourMessageKey] = Resource.StartTour;
        }

        public void Tour(ActionResult result, string message)
        {
            if (!FirstTourPageVisited)
            {
                result = MVC.Voting.EditMyCategories();
            }

            Tour(result != null ? result.GetRouteValueDictionary() : null, message);
        }

        private void Tour(RouteValueDictionary routeValueDictionary, string message)
        {
            if (routeValueDictionary != null)
            {
                TempData[TourLinkKey] = Url.RouteUrl(routeValueDictionary);
            }
            else
            {
                TempData[TourLinkKey] = null;
            }

            TempData[TourMessageKey] = message;
        }

        public void FinishTour()
        {
            IsTour = false;
            TempData[TourMessageKey] = null;
            TempData[TourLinkKey] = null;
        }

        protected override void FillUserInfo(AuthorizationContext filterContext)
        {
            ViewBag.CurrentUser = CurrentUser;
            ViewBag.IsAuthenticated = IsAuthenticated;
            ViewBag.Municipality = Municipality;
            ViewBag.FilteredCategoryIds = WorkContext.CategoryIds;
            ViewBag.LastListUrl = LastListUrl;
            ViewBag.IsGoogleAuthenticated = IsGoogleAuthenticated;

            SetLanguage(CurrentUser.LanguageCode, CurrentUser.LanguageName);
        }

        protected override void Authorize(AuthorizationContext filterContext)
        {
            if (!filterContext.HttpContext.IsDebuggingEnabled && CustomAppSettings.RequireSsl && !Request.Url.GetLeftPart(UriPartial.Authority).Contains("www.lietuva2.lt"))
            {
                if (Request.HttpMethod == "GET" && !IsAjaxRequest)
                {
                    filterContext.Result = RedirectPermanent("https://www.lietuva2.lt" + Request.RawUrl);
                }
                else
                {
                    filterContext.Result = View(MVC.Error.Views.Redirect);
                }
            }

            if (filterContext.HttpContext.Request.IsAuthenticated && filterContext.HttpContext.User != null)
            {
                var isInRole = true;

                //if (SiteMap.CurrentNode != null)
                //{
                //    isInRole = SiteMap.CurrentNode.Roles.Contains("*");
                //    if (!isInRole)
                //    {
                //        isInRole = SiteMap.CurrentNode.Roles.Cast<string>().Where(filterContext.HttpContext.User.IsInRole).Any();
                //    }
                //}

                if (!isInRole)
                {
                    if (filterContext.HttpContext.Request.IsAjaxRequest())
                    {
                        throw new HttpException((int)HttpStatusCode.Unauthorized, "You don't have access. Please log in.");
                    }

                    filterContext.Result = new HttpUnauthorizedResult();
                }
            }
        }



        public virtual ActionResult Back(string returnTo, string defaultReturnTo)
        {
            if (string.IsNullOrEmpty(returnTo))
            {
                if (string.IsNullOrEmpty(defaultReturnTo))
                {
                    return Redirect(FormsAuthentication.DefaultUrl);
                }

                return Redirect(defaultReturnTo);
            }

            return Redirect(Server.UrlDecode(returnTo));
        }

        protected virtual bool EnsureIsUnique()
        {
            if (CurrentUser.RequireUniqueAuthentication && !CurrentUser.IsUnique)
            {
                if (!IsAjaxRequest)
                {
                    TempData[FailureMessageKey] = Globalization.Resources.Account.Resource.ConfirmIdentity;
                    return false;
                }
                else
                {
                    throw new UserNotUniqueException();
                }
            }

            return true;
        }

        protected void SetLanguage(string languageCode, string languageName)
        {
            var lang = new CultureInfo(languageCode)
            {
                DateTimeFormat = { ShortDatePattern = "yyyy-MM-dd" }
            };

            ViewBag.LanguageName = languageName;

            Thread.CurrentThread.CurrentUICulture = lang;
        }

        protected override void OnException(ExceptionContext filterContext)
        {
            if (filterContext.HttpContext.IsCustomErrorEnabled)
            {
                if (filterContext.Exception.GetType() == typeof(UnauthorizedAccessException))
                {
                    filterContext.ExceptionHandled = true;
                    filterContext.Result = View(MVC.Shared.Views.UnauthorizedError);
                }
            }

            base.OnException(filterContext);
        }
    }
}