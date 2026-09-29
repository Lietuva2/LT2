using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using Elmah;
using Framework.Data.Exceptions;
using Framework.Infrastructure.Logging;
using Framework.Mvc.ActionResults;
using Framework.Mvc.Filters;
using Framework.Strings;
using Ninject;

namespace Framework.Mvc.Mvc
{
    /// <summary>
    /// Provides base controller functionality.
    /// </summary>
    public abstract partial class BaseController : Controller
    {
        public BaseController()
        {
        }

        /// <summary>
        /// Success message key.
        /// </summary>
        protected const string SuccessMessageKey = "SuccessMessage";

        /// <summary>
        /// Failure message key.
        /// </summary>
        protected const string FailureMessageKey = "FailureMessage";
        
        [Inject]
        public ILogger Logger { get; set; }

        public bool IsAjaxRequest
        {
            get { return Request.IsAjaxRequest() || !string.IsNullOrEmpty(Request["json"]); }
        }

        public bool IsJsonRequest
        {
            get { return !string.IsNullOrEmpty(Request["json"]); }
        }
        
        /// <summary>
        /// Redirects to the specified routing values and displays success message.
        /// </summary>
        /// <param name="routeValueDictionary">The route value dictionary.</param>
        /// <param name="message">The message.</param>
        /// <returns>Redirect action result.</returns>
        public virtual RedirectToRouteWithTempDataResult RedirectToSuccessAction(RouteValueDictionary routeValueDictionary, string message, bool openDialog = false)
        {
            return new RedirectToRouteWithTempDataResult(routeValueDictionary, SuccessMessageKey, message, openDialog);
        }

        /// <summary>
        /// Redirects to the specified routing values and displays success message.
        /// </summary>
        /// <param name="routeValueDictionary">The route value dictionary.</param>
        /// <param name="message">The message.</param>
        /// <returns>Redirect action result.</returns>
        public virtual RedirectToRouteWithTempDataResult RedirectToSuccessAction(RouteValueDictionary routeValueDictionary, bool openDialog = false)
        {
            return new RedirectToRouteWithTempDataResult(routeValueDictionary, SuccessMessageKey, "Áraðas sëkmingai iðsaugotas", openDialog);
        }

        /// <summary>
        /// Redirects to the specified routing values and displays failure message.
        /// </summary>
        /// <param name="routeValueDictionary">The route value dictionary.</param>
        /// <param name="message">The message.</param>
        /// <returns>Redirect action result.</returns>
        public virtual RedirectToRouteWithTempDataResult RedirectToFailureAction(RouteValueDictionary routeValueDictionary, string message, bool openDialog = false)
        {
            return new RedirectToRouteWithTempDataResult(routeValueDictionary, FailureMessageKey, message, openDialog);
        }

        /// <summary>
        /// Performs save action and redirects to the specified routing values afterwards.
        /// </summary>
        /// <param name="save">The save action.</param>
        /// <param name="routeValueDictionary">The route value dictionary.</param>
        /// <returns>Redirect action result.</returns>
        public virtual RedirectToRouteWithTempDataResult SaveAndRedirect(Action save, RouteValueDictionary routeValueDictionary)
        {
            save();

            return RedirectToSuccessAction(routeValueDictionary, "Áraðas sëkmingai iðsaugotas");
        }

        /// <summary>
        /// Performs delete action and redirects to the specified routing values afterwards.
        /// </summary>
        /// <param name="delete">The delete action.</param>
        /// <param name="routeValueDictionary">The route value dictionary.</param>
        /// <returns>Redirect action result.</returns>
        public virtual RedirectToRouteWithTempDataResult DeleteAndRedirect(Action delete, RouteValueDictionary routeValueDictionary)
        {
            try
            {
                delete();
            }
            catch (SqlDeleteConflictException)
            {
                return RedirectToFailureAction(routeValueDictionary, "Áraðas turi susijusiø áraðø, todël trynimas negalimas");
            }

            return RedirectToSuccessAction(routeValueDictionary, "Áraðas sëkmingai paðalintas");
        }

        /// <summary>
        /// Initializes data that might not be available when the constructor is called.
        /// </summary>
        /// <param name="requestContext">The HTTP context and route data.</param>
        protected override void Initialize(RequestContext requestContext)
        {
            base.Initialize(requestContext);

            var lt = new CultureInfo("lt-LT")
            {
                DateTimeFormat = { ShortDatePattern = "yyyy-MM-dd" }
            };
            Thread.CurrentThread.CurrentCulture = lt;
            Thread.CurrentThread.CurrentUICulture = lt;
        }

        /// <summary>
        /// Called when authorization occurs.
        /// </summary>
        /// <param name="filterContext">Information about the current request and action.</param>
        protected override void OnAuthorization(AuthorizationContext filterContext)
        {
            /*if (!filterContext.HttpContext.Request.IsAuthenticated && filterContext.HttpContext.Request.IsAjaxRequest())
            {
                throw new HttpException((int)HttpStatusCode.Unauthorized, "You don't have access. Please log in.");
            }*/

            FillUserInfo(filterContext);
            Authorize(filterContext);
        }

        /// <summary>
        /// Called when an unhandled exception occurs in the action.
        /// </summary>
        /// <param name="filterContext">Information about the current request and action.</param>
        protected override void OnException(ExceptionContext filterContext)
        {
            if (filterContext.Exception == null)
            {
                return;
            }

            var context = System.Web.HttpContext.Current;
            if (context.IsCustomErrorEnabled)
            {
                ErrorSignal.FromContext(context).Raise(filterContext.Exception, context);
            }

            if (filterContext.HttpContext.Request.IsAjaxRequest())
            {
                var statusCode = filterContext.Exception is HttpException ?
                                (filterContext.Exception as HttpException).GetHttpCode() :
                                (int)HttpStatusCode.InternalServerError;
                filterContext.HttpContext.Response.StatusCode = statusCode;
                filterContext.Result = new JsonResult
                {
                    JsonRequestBehavior = JsonRequestBehavior.AllowGet,
                    Data = new
                    {
                        filterContext.Exception.Message,
                        filterContext.Exception.StackTrace,
                        Url = filterContext.HttpContext.Request.RawUrl
                    }
                };
                filterContext.ExceptionHandled = true;
            }

            base.OnException(filterContext);
        }

        /// <summary>
        /// Logs the exception.
        /// </summary>
        /// <param name="filterContext">The filter context.</param>
        protected virtual void LogException(ExceptionContext filterContext)
        {
            var message = new StringBuilder();
            string user = "User: ";
            user += filterContext.HttpContext.User.Identity.Name;

            message.AppendLine(user);
            message.AppendLine("URL: " + filterContext.HttpContext.Request.Url.AbsoluteUri);
            message.AppendLine("Error: " + filterContext.Exception.Message);

            Logger.Error(message.ToString(), filterContext.Exception);
        }

        protected string RenderPartialViewToString(string viewName)
        {
            return RenderPartialViewToString(viewName, null);
        }

        protected string RenderPartialViewToString(string viewName, object model)
        {
            ViewData.Model = model;

            using (StringWriter sw = new StringWriter())
            {
                ViewEngineResult viewResult = ViewEngines.Engines.FindPartialView(ControllerContext, viewName);
                ViewContext viewContext = new ViewContext(ControllerContext, viewResult.View, ViewData, TempData, sw);
                viewResult.View.Render(viewContext, sw);

                return sw.GetStringBuilder().ToString();
            }
        }

        protected JsonpResult Jsonp(object data)
        {
            return Jsonp(data, null, null, JsonRequestBehavior.AllowGet);
        }

        protected JsonpResult Jsonp(object data, string contentType)
        {
            return Jsonp(data, contentType, null, JsonRequestBehavior.DenyGet);
        }

        protected JsonpResult Jsonp(object data, JsonRequestBehavior behavior)
        {
            return Jsonp(data, null, null, behavior);
        }

        protected virtual JsonpResult Jsonp(object data, string contentType, Encoding contentEncoding)
        {
            return Jsonp(data, contentType, contentEncoding, JsonRequestBehavior.AllowGet);
        }

        protected JsonpResult Jsonp(object data, string contentType, JsonRequestBehavior behavior)
        {
            return Jsonp(data, contentType, null, behavior);
        }

        protected virtual JsonpResult Jsonp(object data, string contentType, Encoding contentEncoding, JsonRequestBehavior behavior)
        {
            var result = new JsonpResult();
            result.Data = data;
            result.ContentType = contentType;
            result.ContentEncoding = contentEncoding;
            result.JsonRequestBehavior = behavior;
            return result;

        }

        /// <summary>
        /// Fills information about the current user.
        /// </summary>
        /// <param name="filterContext">The filter context.</param>
        protected abstract void FillUserInfo(AuthorizationContext filterContext);

        /// <summary>
        /// Checks whether logged in user belongs to one of required roles.
        /// </summary>
        /// <param name="filterContext">The filter context.</param>
        protected abstract void Authorize(AuthorizationContext filterContext);

        protected string GetErrorMessages()
        {
            return ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).Concatenate(";");
        }
    }
}