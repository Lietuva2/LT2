using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Microsoft.AspNet.SignalR;
using Microsoft.Owin;
using Owin;
using Web.App_Start;
using Web.Infrastructure.SignalR;

namespace Web
{
    public partial class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            ConfigureAuth(app);

            HangfireConfig.Configure(app);

            app.MapSignalR(new HubConfiguration
            {
                EnableDetailedErrors = HttpContext.Current.IsDebuggingEnabled,
                Resolver = new NinjectSignalRDependencyResolver(NinjectWebCommon.Bootstrapper.Kernel),
                EnableJavaScriptProxies = true
            });

        }
    }
}