using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using LinkedIn;

namespace Web.Infrastructure.Authentication
{
    public class LinkedInAuthorizeAttribute : ActionFilterAttribute
    {
        private HttpSessionStateBase Session { get; set; }
        private HttpApplicationStateBase Application { get; set; }

        private string AccessToken
        {
            get { return (string)Session["AccessToken"]; }
            set { Session["AccessToken"] = value; }
        }

        private InMemoryTokenManager TokenManager
        {
            get
            {
                var tokenManager = (InMemoryTokenManager)Application["TokenManager"];
                if (tokenManager == null)
                {
                    string consumerKey = ConfigurationManager.AppSettings["LinkedInConsumerKey"];
                    string consumerSecret = ConfigurationManager.AppSettings["LinkedInConsumerSecret"];
                    if (string.IsNullOrEmpty(consumerKey) == false)
                    {
                        tokenManager = new InMemoryTokenManager(consumerKey, consumerSecret);
                        Application["TokenManager"] = tokenManager;
                    }
                }

                return tokenManager;
            }
        }

        protected WebOAuthAuthorization Authorization
        {
            get;
            private set;
        }

        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            this.Session = filterContext.HttpContext.Session;
            this.Application = filterContext.HttpContext.Application;

            

            this.Authorization = new WebOAuthAuthorization(this.TokenManager, this.AccessToken);
                string accessToken = this.Authorization.CompleteAuthorize();
                if (accessToken != null)
                {
                    this.AccessToken = accessToken;
                }

                if (AccessToken == null)
                {
                    this.Authorization.BeginAuthorize();
                    filterContext.Result = new EmptyResult();
                }

            filterContext.Controller.ViewBag.LinkedInAuthorization = this.Authorization;

            base.OnActionExecuting(filterContext);
        }
    }
}