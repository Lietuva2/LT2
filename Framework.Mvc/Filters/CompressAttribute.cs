using System;
using System.IO.Compression;
using System.Web.Mvc;

namespace Framework.Mvc.Filters
{
    /// <summary>
    /// Compresses the response using GZip or Deflate format.
    /// </summary>
    public class CompressAttribute : ActionFilterAttribute
    {
        /// <summary>
        /// Called by the MVC framework before the action method executes.
        /// </summary>
        /// <param name="filterContext">The filter context.</param>
        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            var encodingsAccepted = filterContext.HttpContext.Request.Headers["Accept-Encoding"];
            if (!String.IsNullOrEmpty(encodingsAccepted))
            {
                encodingsAccepted = encodingsAccepted.ToLowerInvariant();
                var response = filterContext.HttpContext.Response;

                if (encodingsAccepted.Contains("gzip"))
                {
                    response.AppendHeader("Content-encoding", "gzip");
                    response.Filter = new GZipStream(response.Filter, CompressionMode.Compress);
                }
                else if (encodingsAccepted.Contains("deflate"))
                {
                    response.AppendHeader("Content-encoding", "deflate");
                    response.Filter = new DeflateStream(response.Filter, CompressionMode.Compress);
                }
            }
        }
    }
}
