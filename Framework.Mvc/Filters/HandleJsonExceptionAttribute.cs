using System.Net;
using System.Web;
using System.Web.Mvc;

namespace Framework.Mvc.Filters
{
    /// <summary>
    /// Handles JSON exceptions gracefully.
    /// </summary>
    public class HandleJsonExceptionAttribute : ActionFilterAttribute
    {
        /// <summary>
        /// Called by the MVC framework after the action method executes.
        /// </summary>
        /// <param name="filterContext">The filter context.</param>
        public override void OnActionExecuted(ActionExecutedContext filterContext)
        {
            if (filterContext.HttpContext.Request.IsAjaxRequest() && filterContext.Exception != null)
            {
                var statusCode = filterContext.Exception is HttpException ? (filterContext.Exception as HttpException).ErrorCode : (int)HttpStatusCode.InternalServerError;
                filterContext.HttpContext.Response.StatusCode =  statusCode;
                filterContext.Result = new JsonResult
                {
                    JsonRequestBehavior = JsonRequestBehavior.AllowGet,
                    Data = new
                    {
                        filterContext.Exception.Message,
                        filterContext.Exception.StackTrace
                    }
                };
                filterContext.ExceptionHandled = true;
            }
        }
    }
}