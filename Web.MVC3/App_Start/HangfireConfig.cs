using System.Collections.Generic;
using System.Configuration;
using Framework.Enums;
using Framework.Infrastructure;
using Microsoft.Owin;
using Services.ModelServices;

using Hangfire;
using Hangfire.Dashboard;
using Owin;

namespace Web
{
    /// <summary>
    /// Only signed-in site administrators (role stored in the database) may open the Hangfire dashboard.
    /// </summary>
    public class SiteAdminDashboardAuthorizationFilter : IAuthorizationFilter
    {
        public bool Authorize(IDictionary<string, object> owinEnvironment)
        {
            var user = new OwinContext(owinEnvironment).Authentication.User;
            if (user == null || !user.Identity.IsAuthenticated || string.IsNullOrEmpty(user.Identity.Name))
            {
                return false;
            }

            var info = ServiceLocator.Resolve<UserService>().GetUserInfoByUserName(user.Identity.Name);
            return info != null && info.Role == UserRoles.Admin;
        }
    }

    public class HangfireConfig
    {
        public static void Configure(IAppBuilder app)
        {
            var config = GlobalConfiguration.Configuration.UseSqlServerStorage("Data.Properties.Settings.LT2_ReportingConnectionString");
            if (ConfigurationManager.AppSettings["QueueName"] != null)
            {
                config.UseMsmqQueues(ConfigurationManager.AppSettings["QueueName"]);
            }

            app.UseHangfireServer();

            var options = new DashboardOptions
            {
                AuthorizationFilters = new IAuthorizationFilter[]
                {
                    new SiteAdminDashboardAuthorizationFilter()
                }
            };

            app.UseHangfireDashboard("/hangfire", options);
        }
    }
}