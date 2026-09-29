using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Web.Infrastructure.Authentication
{
    public static class AuthenticationTypes
    {
        public const string ApplicationCookie = "ApplicationCookie";
        public const string ExternalCookie = "ExternalCookie";
        public const string Viisp = "VIISP";
        public const string Lt2Sso = "LT2SSO";
    }
}