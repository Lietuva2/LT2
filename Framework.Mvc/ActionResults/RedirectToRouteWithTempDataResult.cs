using System.Collections.Generic;
using System.Web.Mvc;
using System.Web.Routing;

namespace Framework.Mvc.ActionResults
{
    /// <summary>
    /// Represents a result that performs a redirection while saving information into the TempData.
    /// </summary>
    public class RedirectToRouteWithTempDataResult : RedirectToRouteResult
    {
        /// <summary>
        /// The pairs of the key / value to insert into the TempData.
        /// </summary>
        private readonly List<KeyValuePair<string, object >> items = new List<KeyValuePair<string, object>>();

        /// <summary>
        /// Initializes a new instance of the <see cref="RedirectToRouteWithTempDataResult"/> class.
        /// </summary>
        /// <param name="routeValues">The route values.</param>
        /// <param name="key">Item key to insert into the TempData.</param>
        /// <param name="value">Item value to insert into the TempData.</param>
        public RedirectToRouteWithTempDataResult(RouteValueDictionary routeValues, string key, object value, bool openDialog = false)
            : base(routeValues)
        {
            items.Add(new KeyValuePair<string, object>(key, value));
            if(openDialog)
            {
                items.Add(new KeyValuePair<string, object>("OpenDialog", true));
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RedirectToRouteWithTempDataResult"/> class.
        /// </summary>
        /// <param name="routeValues">The route values.</param>
        /// <param name="items">The pairs of the key /value to insert into the TempData.</param>
        public RedirectToRouteWithTempDataResult(RouteValueDictionary routeValues, IEnumerable<KeyValuePair<string, object >> items)
            : base(routeValues)
        {
            this.items.AddRange(items);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RedirectToRouteWithTempDataResult"/> class.
        /// </summary>
        /// <param name="routeName">The name of the route.</param>
        /// <param name="routeValues">The route values.</param>
        /// <param name="key">Item key to insert into the TempData.</param>
        /// <param name="value">Item value to insert into the TempData.</param>
        public RedirectToRouteWithTempDataResult(string routeName, RouteValueDictionary routeValues, string key, object value)
            : base(routeName, routeValues)
        {
            items.Add(new KeyValuePair<string, object>(key, value));
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RedirectToRouteWithTempDataResult"/> class.
        /// </summary>
        /// <param name="routeName">The name of the route.</param>
        /// <param name="routeValues">The route values.</param>
        /// <param name="items">The pairs of the key /value to insert into the TempData.</param>
        public RedirectToRouteWithTempDataResult(string routeName, RouteValueDictionary routeValues, IEnumerable<KeyValuePair<string, object>> items)
            : base(routeName, routeValues)
        {
            this.items.AddRange(items);
        }
        
        /// <summary>
        /// Enables processing of the result of an action method by a custom type that inherits from the <see cref="T:System.Web.Mvc.ActionResult"/> class.
        /// </summary>
        /// <param name="context">The context within which the result is executed.</param>
        /// <exception cref="T:System.ArgumentNullException">The <paramref name="context"/> parameter is null.</exception>
        public override void ExecuteResult(ControllerContext context)
        {
            foreach (var item in items)
            {
                if (!context.Controller.TempData.ContainsKey(item.Key))
                {
                    context.Controller.TempData.Add(item.Key, item.Value);
                }
            }

            base.ExecuteResult(context);
        }
    }
}