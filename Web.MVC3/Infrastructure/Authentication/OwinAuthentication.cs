using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Web.Infrastructure.Authentication
{
    public class OwinAuthentication : IAuthentication
    {
        private readonly OwinAuthenticationService service;
        public OwinAuthentication(OwinAuthenticationService service)
        {
            this.service = service;
        }

        public string DefaultUrl { get { return "~/"; } }
        public string SignIn(string username)
        {
            return SignIn(username, false);
        }

        public string SignIn(string username, bool rememberMe)
        {
            service.SignIn(username, null, rememberMe, AuthenticationTypes.ApplicationCookie);
            return null;
        }

        public string SingOut()
        {
            service.SignOut();
            return null;
        }
    }
}