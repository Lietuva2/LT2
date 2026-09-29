using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Security.Claims;
using System.Web;
using System.Web.Configuration;
using Data.ViewModels.Account;
using Microsoft.Owin.Security;

namespace Web.Infrastructure.Authentication
{
    public class OwinAuthenticationService
    {
        public virtual TimeSpan SessionTimeOut
        {
            get
            {
                if (HttpContext.Current != null)
                {
                    if (HttpContext.Current.Session != null)
                    {
                        return TimeSpan.FromMinutes(HttpContext.Current.Session.Timeout);
                    }
                }

                var section = ConfigurationManager.GetSection("system.web/sessionState") as SessionStateSection;
                if (section == null)
                {
                    return TimeSpan.FromMinutes(20);
                }

                return section.Timeout;
            }
        }

        protected IAuthenticationManager AuthenticationManager
        {
            get
            {
                return HttpContext.Current.GetOwinContext().Authentication;
            }
        }

        public virtual void SignIn(string userName, string[] roles, bool rememberMe, string authenticationType = null)
        {
            this.AuthenticationManager.SignOut(AuthenticationTypes.ExternalCookie);

            ClaimsIdentity identity = new ClaimsIdentity(AuthenticationTypes.ApplicationCookie, ClaimTypes.Name, ClaimTypes.Role);
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userName));
            identity.AddClaim(new Claim(ClaimTypes.Name, userName));
            identity.AddClaim(new Claim("http://schemas.microsoft.com/accesscontrolservice/2010/07/claims/identityprovider", "Custom Identity provider"));
            identity.AddClaim(new Claim(ClaimTypes.AuthenticationMethod, authenticationType ?? AuthenticationTypes.ApplicationCookie));

            if (roles != null)
            {
                foreach (var role in roles)
                {
                    identity.AddClaim(new Claim(ClaimTypes.Role, role));
                }
            }

            this.AuthenticationManager.SignIn(new AuthenticationProperties() { IsPersistent = rememberMe, ExpiresUtc = GetAuthExpiration(rememberMe) }, identity);
        }

        public virtual DateTimeOffset? GetAuthExpiration(bool rememberMe)
        {
            var expiresIn = rememberMe ? (TimeSpan?)null : SessionTimeOut;
            return expiresIn.HasValue ? (DateTimeOffset?)DateTimeOffset.UtcNow.Add(expiresIn.Value) : null;
        }

        public virtual void SignOut()
        {
            this.AuthenticationManager.SignOut();
        }

        public virtual int? GetAuthenticatedUserId()
        {
            if (HttpContext.Current.User != null)
            {
                var user = this.AuthenticationManager.User;

                if (user.Identity.IsAuthenticated)
                {
                    return int.Parse(user.FindFirst(ClaimTypes.NameIdentifier).Value);
                }
            }

            return null;
        }

        public virtual string GetAuthenticatedUserName()
        {
            if (HttpContext.Current.User != null)
            {
                var user = this.AuthenticationManager.User;

                if (user.Identity.IsAuthenticated)
                {
                    return user.Identity.Name;
                }
            }

            return null;
        }

        public virtual string GetAuthenticatedUserAuthenticationType()
        {
            if (HttpContext.Current.User != null)
            {
                var user = this.AuthenticationManager.User;

                if (user.Identity.IsAuthenticated)
                {
                    var identity = user.Identity as ClaimsIdentity;
                    return identity == null ? null : identity.FindFirst(ClaimTypes.AuthenticationMethod).Value;
                }
            }

            return null;
        }

        public virtual IEnumerable<ExternalAuthenticationDescription> GetExternalAuthenticationTypes()
        {
            return this.AuthenticationManager
                .GetAuthenticationTypes((AuthenticationDescription d) => d.Properties != null && d.Properties.ContainsKey("Caption"))
                .Select(q => new ExternalAuthenticationDescription() { AuthenticationType = q.AuthenticationType, Caption = q.Caption });
        }

        public virtual void Challenge(string authenticationType, string redirectUri)
        {
            var properties = new AuthenticationProperties() { RedirectUri = redirectUri };
            this.AuthenticationManager.Challenge(properties, authenticationType);
        }

        public virtual ClaimsIdentity ExternalAuthenticate()
        {
            var result = this.AuthenticationManager.AuthenticateAsync(AuthenticationTypes.ExternalCookie).Result;
            if (result == null || !result.Identity.IsAuthenticated)
            {
                return null;
            }
            else
            {
                return result.Identity;
            }
        }
    }
}