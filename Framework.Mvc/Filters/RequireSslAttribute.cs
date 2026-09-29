using System;
using System.Configuration;
using System.Web.Mvc;

namespace Framework.Mvc.Filters
{
    /// <summary>
    /// Requires secured HTTPS connection when request comes from the remote computer or debugging is disabled.
    /// </summary>
    public class RequireSslAttribute : RequireHttpsAttribute
    {
        /// <summary>
        /// Handles unsecured HTTP requests that are sent to the action method.
        /// </summary>
        /// <param name="filterContext">An object that encapsulates information that is required in order to use the <see cref="T:System.Web.Mvc.RequireHttpsAttribute"/> attribute.</param>
        /// <exception cref="T:System.InvalidOperationException">The HTTP request contains an invalid transfer method override. All GET requests are considered invalid.</exception>
        protected override void HandleNonHttpsRequest(AuthorizationContext filterContext)
        {
            if (filterContext.HttpContext.Request.IsLocal || !Convert.ToBoolean(ConfigurationManager.AppSettings["RequireSsl"] ?? "true"))
            {
                return;
            }
            
            base.HandleNonHttpsRequest(filterContext);
        }
    }
}