using System;
using System.Security.Principal;
using System.Web;
using System.Web.Mvc;
using System.Web.SessionState;

using Framework.Data;
using Framework.Infrastructure;
using Services.ModelServices;

namespace Services.Session
{
    public static class MembershipSession
    {
        public static UserInfo GetUser(HttpSessionStateBase session = null, IIdentity identity = null)
        {
            if (session == null)
            {
                if (HttpContext.Current == null || HttpContext.Current.Session == null)
                {
                    return null;
                }

                session = new HttpSessionStateWrapper(HttpContext.Current.Session);
            }

            if (session == null)
            {
                return null;
            }

            if (identity == null)
            {
                if (HttpContext.Current == null || HttpContext.Current.User == null)
                {
                    return null;
                }

                identity = HttpContext.Current.User.Identity;
            }

            var sessionUser = session["UserInfo"] as UserInfo;
            if (sessionUser != null && sessionUser.VerificationPending)
            {
                var service = ServiceLocator.Resolve<UserService>();
                var user = service.GetUserInfoByUserName(identity.Name);
                
                if (user != null && user.IsUnique)
                {
                    sessionUser.FirstName = user.FirstName;
                    sessionUser.LastName = user.LastName;
                    sessionUser.PersonCode = user.PersonCode;
                    sessionUser.AuthenticationSource = user.AuthenticationSource;
                    sessionUser.VerificationPending = false;
                }

                session["UserInfo"] = sessionUser;
            }
            else if (sessionUser == null || !sessionUser.UserName.Trim().Equals(identity.Name.Trim(), StringComparison.CurrentCultureIgnoreCase))
            {
                if (!identity.IsAuthenticated)
                {
                    if (sessionUser != null && !(session["UserInfo"] as UserInfo).IsAuthenticated)
                    {
                        return sessionUser;
                    }

                    session["UserInfo"] = GetAnonymousUser();
                }
                else
                {
                    var service = ServiceLocator.Resolve<UserService>();
                    var user = service.GetUserInfoByUserName(identity.Name);
                    if (user == null)
                    {
                        user = GetAnonymousUser();
                    }

                    session["UserInfo"] = user;
                }
            }

            return session["UserInfo"] as UserInfo;
        }

        public static void SetUser(UserInfo user)
        {
            HttpContext.Current.Session["UserInfo"] = user;
        }

        public static void Reset()
        {
            HttpContext.Current.Session["UserInfo"] = null;
        }

        private static UserInfo GetAnonymousUser()
        {
            return new UserInfo()
            {
                FirstName = "Anoniminis",
                LastName = "Naudotojas",
                UserName = "anonymous",
                LanguageCode = "lt-LT",
                LanguageName = "Lietuvių",
                IsAuthenticated = false,
                Ip = HttpContext.Current.Request.UserHostAddress
            };
        }
    }
}
