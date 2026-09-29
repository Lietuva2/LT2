using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using Framework.Infrastructure;
using Services.ModelServices;

namespace Web.Infrastructure.Routing
{
    public class ShortLinkRouteConstraint : IRouteConstraint
    {
        public bool Match(HttpContextBase httpContext, Route route, string parameterName,
                          RouteValueDictionary values, RouteDirection routeDirection)
        {
            var service = ServiceLocator.Resolve<ShortLinkService>();
            return service.ShortLinkExists(values["id"].ToString());
        }
    }
}