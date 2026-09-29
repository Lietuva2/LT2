using System;
using System.Web;
using System.Web.Mvc;

namespace Framework.Mvc.Filters
{
    /// <summary>
    /// Marks an action that changes state but is reachable with GET (links, image URLs, JSONP).
    /// <see cref="SameOriginRequestFilter"/> refuses such requests when they come from another site.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class SameOriginAttribute : Attribute
    {
    }

    /// <summary>
    /// Marks an action that legitimately receives requests from other sites
    /// (bank link, VIISP and payment callbacks). Such actions must verify the request themselves.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class AllowCrossOriginAttribute : Attribute
    {
    }

    /// <summary>
    /// Global CSRF protection that needs no tokens in the views: every non-GET request and every GET request to an
    /// action marked with <see cref="SameOriginAttribute"/> must carry an Origin or Referer header of this site.
    /// Browsers always send one of them on same-site form posts and XHR, and a cross-site page cannot forge them.
    /// Requests with neither header are refused too.
    /// </summary>
    public class SameOriginRequestFilter : IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationContext filterContext)
        {
            if (IsDefined<AllowCrossOriginAttribute>(filterContext))
            {
                return;
            }

            var request = filterContext.HttpContext.Request;
            var method = request.HttpMethod.ToUpperInvariant();
            var isSafeMethod = method == "GET" || method == "HEAD" || method == "OPTIONS";

            if (isSafeMethod && !IsDefined<SameOriginAttribute>(filterContext))
            {
                return;
            }

            if (!IsSameOrigin(request))
            {
                filterContext.Result = new HttpStatusCodeResult(403, "Cross-site request refused");
            }
        }

        private static bool IsDefined<T>(AuthorizationContext filterContext) where T : Attribute
        {
            return filterContext.ActionDescriptor.IsDefined(typeof(T), true) ||
                   filterContext.ActionDescriptor.ControllerDescriptor.IsDefined(typeof(T), true);
        }

        private static bool IsSameOrigin(HttpRequestBase request)
        {
            var host = request.Url.Authority;

            var origin = request.Headers["Origin"];
            if (!string.IsNullOrEmpty(origin))
            {
                Uri originUri;
                return origin != "null" &&
                       Uri.TryCreate(origin, UriKind.Absolute, out originUri) &&
                       string.Equals(originUri.Authority, host, StringComparison.OrdinalIgnoreCase);
            }

            Uri referrer;
            try
            {
                referrer = request.UrlReferrer;
            }
            catch (UriFormatException)
            {
                return false;
            }

            return referrer != null && string.Equals(referrer.Authority, host, StringComparison.OrdinalIgnoreCase);
        }
    }
}
