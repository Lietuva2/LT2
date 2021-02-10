using System.Configuration;

using Hangfire;
using Hangfire.Dashboard;
using Owin;

namespace Web
{
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
                    new AuthorizationFilter { Users = "satanod" }
                }
            };

            app.UseHangfireDashboard("/hangfire", options);
        }
    }
}