using System;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Owin;
using Microsoft.Owin.Logging;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.Infrastructure;
using Owin;

namespace Web.Infrastructure.Owin
{
    public class SsoAuthenticationMiddleware : AuthenticationMiddleware<SsoAuthenticationOptions>
    {
        public SsoAuthenticationMiddleware(OwinMiddleware next, IAppBuilder app, SsoAuthenticationOptions options)
            : base(next, options)
        {
            if (String.IsNullOrEmpty(Options.SignInAsAuthenticationType))
            {
                Options.SignInAsAuthenticationType = app.GetDefaultSignInAsAuthenticationType();
            }
        }

        protected override AuthenticationHandler<SsoAuthenticationOptions> CreateHandler()
        {
            return new SsoAuthenticationHandler();
        }
    }
}
