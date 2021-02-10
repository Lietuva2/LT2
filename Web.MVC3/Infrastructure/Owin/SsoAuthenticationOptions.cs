using Microsoft.Owin;
using Microsoft.Owin.Security;
using Web.Infrastructure.Authentication;

namespace Web.Infrastructure.Owin
{
    public class SsoAuthenticationOptions : AuthenticationOptions
    {
        public SsoAuthenticationOptions()
            : base(AuthenticationTypes.Lt2Sso)
        {
            Caption = AuthenticationTypes.Lt2Sso;
            CallbackPath = new PathString("/signin-lt2");
            AuthenticationMode = AuthenticationMode.Passive;
        }

        /// <summary>
        /// Gets or sets the request path within the application's base path where the user-agent will be returned.
        /// The middleware will process this request when it arrives.
        /// Default value is "/signin-viisp".
        /// </summary>
        public PathString CallbackPath { get; set; }

        /// <summary>
        /// Gets or sets the name of another authentication middleware which will be responsible for actually issuing a user <see cref="System.Security.Claims.ClaimsIdentity"/>.
        /// </summary>
        public string SignInAsAuthenticationType { get; set; }

        public string Caption
        {
            get { return Description.Caption; }
            set { Description.Caption = value; }
        }
    }
}
