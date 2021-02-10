using System;
using System.Web.Mvc;
using Framework.Mvc.Mvc;

namespace Framework.Mvc.Filters
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = true)]
    public class AuthorizeAttribute : System.Web.Mvc.AuthorizeAttribute
    {
        public override void OnAuthorization(AuthorizationContext filterContext)
        {
            base.OnAuthorization(filterContext);
        }

        protected override void HandleUnauthorizedRequest(System.Web.Mvc.AuthorizationContext filterContext)
        {
            var user = filterContext.HttpContext.User;
            var controller = filterContext.Controller as BaseController;
            if (!user.Identity.IsAuthenticated && (filterContext.HttpContext.Request.IsAjaxRequest() || (controller != null && controller.IsAjaxRequest)))
            {
                filterContext.Result = new JsonResult()
                                           {
                                               JsonRequestBehavior = JsonRequestBehavior.AllowGet,
                                               Data = new
                                                          {
                                                              Message = "UnauthorizedAccessException: You don't have access. Please log in."
                                                          }
                                           };
            }
            else
            {
                base.HandleUnauthorizedRequest(filterContext);
            }
        }
    }
}
