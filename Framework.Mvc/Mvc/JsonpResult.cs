using System;
using System.Text.RegularExpressions;
using System.Web.Mvc;

namespace Framework.Mvc.Mvc
{
    public class JsonpResult : JsonResult
    {
        private static readonly Regex ValidCallback = new Regex(@"^[A-Za-z_$][\w$]{0,63}(\.[A-Za-z_$][\w$]{0,63}){0,4}$", RegexOptions.Compiled);

        public override void ExecuteResult(ControllerContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }
            var request = context.HttpContext.Request;
            var response = context.HttpContext.Response;
            string jsoncallback = (context.RouteData.Values["callback"] as string) ?? request["callback"];
            if (!string.IsNullOrEmpty(jsoncallback) && !ValidCallback.IsMatch(jsoncallback))
            {
                jsoncallback = null;
            }
            if (!string.IsNullOrEmpty(jsoncallback))
            {
                if (string.IsNullOrEmpty(base.ContentType))
                {
                    base.ContentType = "application/x-javascript";
                }
                response.Write(string.Format("{0}(", jsoncallback));
            }
            base.ExecuteResult(context);
            if (!string.IsNullOrEmpty(jsoncallback))
            {
                response.Write(")");
            }
        }
    }
}
