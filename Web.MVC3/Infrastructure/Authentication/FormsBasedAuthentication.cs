using System;
using System.Web.Security;

namespace Web.Infrastructure.Authentication
{
    /// <summary>
    /// Forms-based authentication.
    /// </summary>
    public class FormsBasedAuthentication : IAuthentication
    {
        /// <summary>
        /// Gets the default URL.
        /// </summary>
        /// <value>The default URL.</value>
        public string DefaultUrl
        {
            get
            {
                return FormsAuthentication.DefaultUrl;
            }
        }

        public string LoginUrl
        {
            get { return FormsAuthentication.LoginUrl; }
        }

        public string SignIn(string username)
        {
            return SignIn(username, false);
        }

        /// <summary>
        /// Signs specified user in.
        /// </summary>
        /// <param name="username">The username to sign in.</param>
        /// <returns>The URL to redirect user to.</returns>
        public string SignIn(string username, bool rememberMe)
        {
            FormsAuthentication.SetAuthCookie(username, rememberMe);
            
            var url = FormsAuthentication.GetRedirectUrl(username, false);
            if (String.IsNullOrEmpty(url) || url == "/")
            {
                url = FormsAuthentication.DefaultUrl;
            }

            return url;
        }

        /// <summary>
        /// Sings current out.
        /// </summary>
        /// <returns>The URL to redirect user to.</returns>
        public string SingOut()
        {
            FormsAuthentication.SignOut();

            return FormsAuthentication.LoginUrl;
        }
    }
}
