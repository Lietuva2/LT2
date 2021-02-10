using System;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using Framework.Mvc;
using Framework.Mvc.Mvc;
using Services.Session;

namespace Web.Infrastructure.Attributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = true)]
    public class AuthorizeAttribute : Framework.Mvc.Filters.AuthorizeAttribute
    {
        private bool allowAnonymous = false;

        public AuthorizeAttribute()
        {
            this.allowAnonymous = false;
        }

        public AuthorizeAttribute(bool allowAnonymous)
        {
            this.allowAnonymous = allowAnonymous;
        }

        public override void OnAuthorization(AuthorizationContext filterContext)
        {
            var user = MembershipSession.GetUser();
            if (user.IsAuthenticated && (string.IsNullOrEmpty(user.Email) || !user.HasSigned))
            {
                this.HandleUnauthorizedRequest(filterContext);
            }
            else
            {
                base.OnAuthorization(filterContext);
            }
        }

        protected override void HandleUnauthorizedRequest(AuthorizationContext filterContext)
        {
            var user = MembershipSession.GetUser();
            var controller = filterContext.Controller as BaseController;
            if (user.IsAuthenticated && (string.IsNullOrEmpty(user.Email) || !user.HasSigned))
            {
                string manifestController = "Account";
                string manifestAction = "Manifest";
                if (!(filterContext.RouteData.Values["controller"].ToString() == manifestController && filterContext.RouteData.Values["action"].ToString() == manifestAction))
                {
                    if (controller.IsAjaxRequest)
                    {
                        filterContext.Result = new JsonResult()
                        {
                            JsonRequestBehavior = JsonRequestBehavior.AllowGet,
                            Data = new
                            {
                                Message = "SignRequired: Patvirtinkite el. paštą ir sutikite su taisyklėmis.",
                                Url =
                     new UrlHelper(
                     ((MvcHandler)HttpContext.Current.Handler).RequestContext).
                     Action(manifestAction, manifestController)
                            }
                        };
                    }
                    else
                    {
                        var routeVals = new RouteValueDictionary() { { "controller", manifestController }, { "action", manifestAction } };
                        filterContext.Result = new RedirectToRouteResult(routeVals);

                    }
                }
            }
            else if (!allowAnonymous)
            {
                base.HandleUnauthorizedRequest(filterContext);
            }
        }
    }
}
