using System;
using System.Web.Mvc;

namespace Framework.Mvc.Mvc
{
    public static class SafeRedirectExtensions
    {
        /// <summary>
        /// Returns <paramref name="target"/> when it points to this site (a local path or an absolute URL on this host),
        /// otherwise <paramref name="fallback"/>. Use it for every redirect to a URL taken from the request,
        /// so the site cannot be used to send visitors to another site (open redirect).
        /// </summary>
        public static string LocalOrDefault(this UrlHelper url, string target, string fallback = "~/")
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                return fallback;
            }

            if (url.IsLocalUrl(target))
            {
                return target;
            }

            Uri uri;
            var request = url.RequestContext.HttpContext.Request;
            if (Uri.TryCreate(target, UriKind.Absolute, out uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
                string.Equals(uri.Authority, request.Url.Authority, StringComparison.OrdinalIgnoreCase))
            {
                return target;
            }

            return fallback;
        }
    }
}
