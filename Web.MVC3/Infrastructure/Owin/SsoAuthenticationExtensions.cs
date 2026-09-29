using System;
using Microsoft.Owin.Extensions;
using Owin;

namespace Web.Infrastructure.Owin
{
    public static class SsoAuthenticationExtensions
    {
        public static IAppBuilder UseLt2Sso(this IAppBuilder app, SsoAuthenticationOptions options)
        {
            if (app == null)
            {
                throw new ArgumentNullException("app");
            }

            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            app.Use(typeof(SsoAuthenticationMiddleware), app, options);
            app.UseStageMarker(PipelineStage.Authenticate);
            return app;
        }
    }
}