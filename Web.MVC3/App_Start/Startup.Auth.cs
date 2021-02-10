using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web;

using Facebook;

using Microsoft.AspNet.SignalR;
using Microsoft.Owin;
using Microsoft.Owin.Logging;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.Cookies;
using Microsoft.Owin.Security.Facebook;
using Microsoft.Owin.Security.MicrosoftAccount;
using Owin;
using Owin.Security.Providers.Instagram;
using Owin.Security.Providers.LinkedIn;
using Owin.Security.Providers.PayPal;
using Owin.Security.Providers.SoundCloud;
using Owin.Security.Providers.Yahoo;
using Services.Infrastructure;
using Web.App_Start;
using Web.Infrastructure.Authentication;
using Web.Infrastructure.Owin;
using Web.Infrastructure.SignalR;

namespace Web
{
    public partial class Startup
    {
        // For more information on configuring authentication, please visit http://go.microsoft.com/fwlink/?LinkId=301864
        public void ConfigureAuth(IAppBuilder app)
        {
            // Enable the application to use a cookie to store information for the signed in user
            app.UseCookieAuthentication(new CookieAuthenticationOptions
            {
                AuthenticationType = AuthenticationTypes.ApplicationCookie,
                ExpireTimeSpan = TimeSpan.FromDays(14),
                LoginPath = new PathString(string.IsNullOrEmpty(CustomAppSettings.SsoBaseUrl) ? "/Account/Login" : "/Account/SsoChallenge"),
                LogoutPath = new PathString("/Account/Logout")
            });

            // Use a cookie to temporarily store information about a user logging in with a third party login provider
            app.SetDefaultSignInAsAuthenticationType(AuthenticationTypes.ExternalCookie);
            app.UseCookieAuthentication(new CookieAuthenticationOptions
            {
                AuthenticationType = AuthenticationTypes.ExternalCookie,
                AuthenticationMode = AuthenticationMode.Passive,
                CookieName = CookieAuthenticationDefaults.CookiePrefix + AuthenticationTypes.ExternalCookie,
                ExpireTimeSpan = TimeSpan.FromMinutes(5),
            });

            if (string.IsNullOrEmpty(CustomAppSettings.SsoBaseUrl))
            {
                var facebookOptions = new FacebookAuthenticationOptions()
                {
                    AppId = ConfigurationManager.AppSettings["FbAppId"],

                    AppSecret = ConfigurationManager.AppSettings["FbAppSecret"],
                    Provider = new FacebookAuthenticationProvider()
                    {
                        OnAuthenticated = context =>
                        {
                            //Get the access token from FB and store it in the database and
                            //use FacebookC# SDK to get more information about the user
                            context.Identity.AddClaim(
                                new Claim(ClaimTypes.Sid,
                                    context.AccessToken));
                            var client = new FacebookClient(context.AccessToken);
                            dynamic me = client.Get("/me?fields=email");
                            if (me.email != null)
                            {
                                context.Identity.AddClaim(new Claim(ClaimTypes.Email, me.email));
                            }

                            return Task.FromResult(0);
                        }
                    }
                };

                facebookOptions.Scope.Add("email");

                app.UseFacebookAuthentication(facebookOptions);

                app.UseGoogleAuthentication(ConfigurationManager.AppSettings["GoogleAppId"],
                    ConfigurationManager.AppSettings["GoogleAppSecret"]);

                app.UseYahooAuthentication("fake", "fake");

                app.UseLinkedInAuthentication(ConfigurationManager.AppSettings["LinkedInConsumerKey"],
                    ConfigurationManager.AppSettings["LinkedInConsumerSecret"]);

                if (ConfigurationManager.AppSettings["MicrosoftConsumerKey"] != null)
                {
                    var options = new MicrosoftAccountAuthenticationOptions()
                    {
                        ClientId = ConfigurationManager.AppSettings["MicrosoftConsumerKey"],
                        ClientSecret = ConfigurationManager.AppSettings["MicrosoftConsumerSecret"]
                    };

                    options.Scope.Add("wl.emails");
                    options.Scope.Add("wl.basic");

                    app.UseMicrosoftAccountAuthentication(options);
                }
            }
            else
            {
                app.UseLt2Sso(new SsoAuthenticationOptions());
            }
        }
    }
}