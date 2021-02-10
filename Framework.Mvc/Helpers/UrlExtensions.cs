using System;
using System.Web.Mvc;

namespace Framework.Mvc.Helpers
{
    public static class UrlExtensions
    {
        public static Uri GetBaseUrl(this UrlHelper url)
        {
            Uri contextUri = new Uri(url.RequestContext.HttpContext.Request.Url, url.RequestContext.HttpContext.Request.RawUrl);
            UriBuilder realmUri = new UriBuilder(contextUri) { Path = url.RequestContext.HttpContext.Request.ApplicationPath, Query = null, Fragment = null };
            return realmUri.Uri;
        }

        public static string ActionAbsolute(this UrlHelper url, string actionName, string controllerName, object routeValues)
        {
            return new Uri(GetBaseUrl(url), url.Action(actionName, controllerName, routeValues)).AbsoluteUri;
        }
    }
}
