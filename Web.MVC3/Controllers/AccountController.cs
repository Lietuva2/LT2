using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Data;
using System.Data.Entity.Core;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security;
using System.Security.Claims;
using System.Text;
using System.Web;
using System.Web.Mvc;
using System.Web.Security;
using Data.Enums;
using Data.ViewModels.Account;
using Data.ViewModels.Base;
using Data.ViewModels.Comments;
using DotNetOpenAuth.OpenId;
using DotNetOpenAuth.OpenId.Extensions.AttributeExchange;
using DotNetOpenAuth.OpenId.Extensions.SimpleRegistration;
using DotNetOpenAuth.OpenId.RelyingParty;
using Elmah;
using Facebook;
using Framework.Data;
using Framework.Enums;
using Framework.Exceptions;
using Framework.Infrastructure.Logging;
using Framework.Lists;
using Framework.Mvc.Filters;
using Framework.Mvc.Lists;
using Framework.Mvc.Security;
using Framework.Strings;
using Globalization.Resources.Account;
using Globalization.Resources.Shared;
using LinkedIn;
using LinkedIn.ServiceEntities;
using Ninject;
using Recaptcha;
using Services.Enums;
using Services.Exceptions;
using Services.Infrastructure;
using Services.ModelServices;
using Services.Session;
using Services.VIISP;
using Web.Infrastructure.Authentication;
using Authorize = Web.Infrastructure.Attributes.AuthorizeAttribute;
using DotNetOpenAuth.Messaging;
using Resource = Globalization.Resources.Account.Resource;

namespace Web.Controllers
{
    public partial class AccountController : SiteBaseServiceController<UserService>
    {
        /// <summary>
        /// Forms based authentication.
        /// </summary>
        private readonly IAuthentication authentication;

        private readonly OwinAuthenticationService authenticationService;

        /// <summary>
        /// Initializes a new instance of the <see cref="UserController"/> class.
        /// </summary>
        /// <param name="authentication">The forms based authentication.</param>
        public AccountController(IAuthentication authentication, OwinAuthenticationService authenticationService)
        {
            this.authentication = authentication;
            this.authenticationService = authenticationService;
        }

        private CommentViews? CommentsSort
        {
            get
            {
                var show = Session["UserCommentsSort"];
                if (show == null)
                {
                    return null;
                }

                return (CommentViews)show;
            }
            set
            {
                Session["UserCommentsSort"] = value;
            }
        }

        private ForAgainst? CommentsFilter
        {
            get
            {
                var show = Session["UserCommentsFilter"];
                if (show == null)
                {
                    return null;
                }

                return (ForAgainst)show;
            }
            set
            {
                Session["UserCommentsFilter"] = value;
            }
        }

        protected Uri LastReferrerUrl
        {
            get { return Session["LastReferrerUrl"] as Uri; }
            set { Session["LastReferrerUrl"] = value; }
        }

        [Inject]
        public ILogger Logger { get; set; }

        private string AddSsoValuesToUrl(string url)
        {
            if (!SsoRedirect)
            {
                return url;
            }

            SsoRedirect = false;
            return url + "&user=" + Uri.EscapeDataString(GetSsoUserInfo());
        }

        public virtual ActionResult SsoChallenge(string returnTo)
        {
            this.authenticationService.SignOut();
            this.authenticationService.Challenge(AuthenticationTypes.Lt2Sso, Url.Action(MVC.Account.ExternalLoginCallback(returnTo)));
            return new HttpUnauthorizedResult();
        }

        [HttpGet, RequireSsl, ImportModelStateFromTempData]
        public virtual ActionResult SsoLogin(string returnUrl)
        {
            SsoRedirect = true;

            if (CurrentUser.IsAuthenticated)
            {
                return Redirect(AddSsoValuesToUrl(returnUrl));
            }

            var model = new UserLoginModel()
            {
                ReturnTo = returnUrl,
                ExternalAuthenticationTypes = this.authenticationService.GetExternalAuthenticationTypes()
            };

            return View(MVC.Account.Views.Login, model);
        }

        /// <summary>
        /// Displays user login view.
        /// </summary>
        /// <returns>Login view.</returns>
        [HttpGet, RequireSsl, ImportModelStateFromTempData]
        public virtual ActionResult Login(string returnUrl)
        {
            if (!string.IsNullOrEmpty(CustomAppSettings.SsoBaseUrl))
            {
                return SsoChallenge(returnUrl);
            }

            var model = new UserLoginModel()
                            {
                                ReturnTo = returnUrl,
                                ExternalAuthenticationTypes = this.authenticationService.GetExternalAuthenticationTypes()
                            };

            return View(MVC.Account.Views.Login, model);
        }

        /// <summary>
        /// Displays user login view.
        /// </summary>
        /// <returns>Login view.</returns>
        [HttpGet, RequireSsl, ImportModelStateFromTempData]
        public virtual ActionResult LoginPartial(string returnUrl)
        {
            var model = new UserLoginModel()
            {
                ReturnTo = returnUrl,
                ExternalAuthenticationTypes = this.authenticationService.GetExternalAuthenticationTypes()
            };

            return PartialView(MVC.Account.Views.LoginPartial, model);
        }

        /// <summary>
        /// Tries to authenticate user.
        /// </summary>
        /// <param name="model">The view model.</param>
        /// <returns>User authenticated and redirected to the default page.</returns>
        [HttpPost, RequireSsl, ExportModelStateToTempData]
        public virtual ActionResult Login(UserLoginModel model)
        {
            var redirectUrl = authentication.DefaultUrl;
            if (!model.ReturnTo.IsNullOrEmpty() && model.ReturnTo != Url.Action(MVC.NewsFeed.Default(CurrentUser.LanguageCode)) && model.ReturnTo != Url.Action(MVC.NewsFeed.Default()))
            {
                redirectUrl = model.ReturnTo;
            }
            var user = Validate(model);

            if (user != null)
            {
                authentication.SignIn(model.UserName, model.RememberMe);
            }
            else
            {
                return RedirectToAction(MVC.Account.Login(redirectUrl));
            }

            if (SsoRedirect)
            {
                return RedirectToAction(MVC.Account.RedirectGateway(redirectUrl));
            }

            if (!user.HasSigned)
            {
                return RedirectToAction(MVC.Account.Manifest());
            }

            if (user.RequireChangePassword)
            {
                MembershipSession.Reset();
                return RedirectToAction(MVC.Account.ChangePassword());
            }

            if (!string.IsNullOrEmpty(model.Json))
            {
                return Jsonp(new { IsValid = CurrentUser.IsAuthenticated, CurrentUser.FullName, SessionId = Session.SessionID });
            }

            return Redirect(redirectUrl);
        }

        [Authorize]
        public virtual ActionResult RedirectGateway(string returnUrl)
        {
            return Redirect(AddSsoValuesToUrl(returnUrl));
        }

        [RequireSsl]
        public virtual ActionResult RemoteLogin(UserLoginModel model)
        {
            if (!IsAjaxRequest)
            {
                return RedirectToAction(MVC.Common.Start());
            }

            var user = Validate(model);

            if (user != null)
            {
                authentication.SignIn(model.UserName, model.RememberMe);
                return Jsonp(new { IsValid = CurrentUser.IsAuthenticated, model.UserName, model.Password, CurrentUser.FullName });
            }

            return Jsonp(new { IsValid = false });
        }

        [HttpGet]
        public virtual ActionResult ChangePassword()
        {
            return View();
        }

        [HttpPost]
        public virtual ActionResult ChangePassword(ChangePasswordModel model)
        {
            if (model.NewPassword.ToLower().Contains(CurrentUser.UserName.ToLower()))
            {
                ModelState.AddModelError("", Resource.PasswordSimilarToUserName);
            }

            if (model.NewPassword.ToLower().Contains(CurrentUser.FirstName.ToLower()) || model.NewPassword.ToLower().Contains(CurrentUser.LastName.ToLower()))
            {
                ModelState.AddModelError("", Resource.PasswordSimilarToName);
            }

            if (ModelState.IsValid && Service.ChangePassword(model))
            {
                return RedirectToAction(MVC.Common.Start());
            }

            ModelState.AddModelError("", Resource.PasswordChangeFailed);

            return View();
        }

        //public virtual ActionResult FacebookConfirm(UserAccountViewModel model)
        //{
        //    var success = Service.ConfirmFacebookUser(Convert.ToInt64(model.FacebookLogin.FacebookId), model.FacebookLogin.FirstName, model.FacebookLogin.LastName, model.FacebookLogin.Email);

        //    if(!success)
        //    {
        //        TempData["FailureMessage"] = string.Format("Facebook naudotojas {0} jau užregistruotas Lietuva 2.0!", model.FacebookLogin.FullName);
        //    }

        //    if (!model.FacebookLogin.IsPageLiked)
        //    {
        //        TempData["FbPageNotLiked"] = true;
        //    }

        //    return Details(null, null, null);
        //}

        public virtual ActionResult FacebookLogin(string accessToken, bool rememberMe, string returnUrl)
        {
            try
            {
                var client = new FacebookClient(accessToken);
                dynamic result = client.Get("me", new {fields = "id,first_name,last_name,username,email"});

                return OpenAuthRegister(new OAuthLoginModel()
                {
                    Email = result.email,
                    FacebookId = result.id,
                    FirstName = result.first_name,
                    LastName = result.last_name,
                    RememberMe = rememberMe,
                    UserName = result.username,
                    IsPageLiked = IsFbPageLiked(client),
                    ReturnTo = returnUrl
                });
            }
            catch (FacebookOAuthException ex)
            {
                ModelState.AddModelError("", Resource.LoginFailed);
                return Login();
            }
        }

        public bool IsFbPageLiked(FacebookClient client)
        {
            dynamic likes = client.Get("/me/likes/" + CustomAppSettings.FbPageId);
            return likes != null && likes.data != null && (likes.data as Facebook.JsonArray) != null &&
                   (likes.data as Facebook.JsonArray).Any();
        }

        public virtual ActionResult OpenIdLogin(string loginIdentifier, string returnTo, bool chkRememberMe)
        {
            var openid = new OpenIdRelyingParty();
            IAuthenticationResponse response = openid.GetResponse();

            if (response != null)
            {
                return OpenIdCallback(response, returnTo);
            }


            if (!Identifier.IsValid(loginIdentifier))
            {
                ModelState.AddModelError("loginIdentifier",
                    "The specified login identifier is invalid");
                return View(MVC.Account.Views.Login, new UserLoginModel() { ReturnTo = returnTo });
            }

            TempData["RememberMe"] = chkRememberMe;
            IAuthenticationRequest request;
            try
            {
                request = openid.CreateRequest(
                    Identifier.Parse(loginIdentifier));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("loginIdentifier", Resource.InvalidCredentials);
                return View(MVC.Account.Views.Login, new UserLoginModel() { ReturnTo = returnTo });
            }
            var fetch = new FetchRequest();
            //ask for more info - the email address
            //no guarantee you'll get it back :)
            fetch.Attributes.Add(new AttributeRequest(WellKnownAttributes.Contact.Email, true));
            fetch.Attributes.Add(new AttributeRequest(WellKnownAttributes.Name.FullName, true));
            fetch.Attributes.Add(new AttributeRequest(WellKnownAttributes.Name.First, true));
            fetch.Attributes.Add(new AttributeRequest(WellKnownAttributes.Name.Last, true));
            fetch.Attributes.Add(new AttributeRequest(WellKnownAttributes.Name.Middle, true));
            request.AddExtension(fetch);
            // Require some additional data
            request.AddExtension(new ClaimsRequest
            {
                Email = DemandLevel.Require,
                FullName = DemandLevel.Require
            });

            return request.RedirectingResponse.AsActionResult();
        }

        private ActionResult OpenIdCallback(IAuthenticationResponse response, string returnTo)
        {
            if (response != null)
            {
                switch (response.Status)
                {
                    case AuthenticationStatus.Authenticated:
                        var fetch = response.GetExtension<FetchResponse>();
                        var claims = response.GetExtension<ClaimsResponse>();
                        string email = null, firstName = null, lastName = null, fullName = null;
                        if (fetch != null)
                        {
                            if (fetch.Attributes.Contains(WellKnownAttributes.Name.First))
                            {
                                firstName = fetch.Attributes[WellKnownAttributes.Name.First].Values.FirstOrDefault();
                            }
                            if (fetch.Attributes.Contains(WellKnownAttributes.Name.Last))
                            {
                                lastName = fetch.Attributes[WellKnownAttributes.Name.Last].Values.FirstOrDefault();
                            }

                            if (fetch.Attributes.Contains(WellKnownAttributes.Name.FullName))
                            {
                                fullName = fetch.Attributes[WellKnownAttributes.Name.FullName].Values.FirstOrDefault();
                            }

                            email = fetch.Attributes[WellKnownAttributes.Contact.Email].Values.FirstOrDefault();
                        }
                        if (claims != null)
                        {
                            if (string.IsNullOrEmpty(email))
                            {
                                email = claims.Email;
                            }

                            if (string.IsNullOrEmpty(firstName) && string.IsNullOrEmpty(lastName) && string.IsNullOrEmpty(fullName))
                            {
                                fullName = claims.FullName;
                            }
                        }

                        if (!string.IsNullOrEmpty(fullName) && string.IsNullOrEmpty(lastName) && string.IsNullOrEmpty(firstName))
                        {
                            if (fullName.IndexOf(' ') > 0)
                            {
                                firstName = fullName.Substring(0, fullName.LastIndexOf(' '));
                                lastName = fullName.Substring(fullName.LastIndexOf(' ') + 1);
                            }
                            else
                            {
                                firstName = fullName;
                                lastName = fullName;
                            }
                        }

                        return OpenAuthRegister(new OAuthLoginModel()
                                       {
                                           ReturnTo = returnTo,
                                           DoRegister = false,
                                           Email = email,
                                           FirstName = firstName,
                                           LastName = lastName,
                                           IsPageLiked = true,
                                           UserName = email,
                                           RememberMe = (bool?)TempData["RememberMe"] ?? false
                                       });
                    case AuthenticationStatus.Canceled:
                        ModelState.AddModelError("loginIdentifier",
                            "Login was cancelled at the provider");
                        break;
                    case AuthenticationStatus.Failed:
                        ModelState.AddModelError("loginIdentifier",
                            "Login failed using the provided OpenID identifier");
                        break;
                }
            }

            return View(MVC.Account.Views.Login, new UserLoginModel() { ReturnTo = returnTo });
        }

        public virtual ActionResult ExternalLogin(string provider, string returnTo, bool chkRememberMe = false)
        {
            if (provider == "Yahoo")
            {
                return RedirectToAction(MVC.Account.OpenIdLogin("http://me.yahoo.com/", returnTo, chkRememberMe));
            }

            this.authenticationService.SignOut();
            TempData["RememberMe"] = chkRememberMe;
            this.authenticationService.Challenge(provider, Url.Action(MVC.Account.ExternalLoginCallback(returnTo)));
            return new HttpUnauthorizedResult();
        }

        public virtual ActionResult ExternalLoginCallback(string returnTo)
        {
            var identity = this.authenticationService.ExternalAuthenticate();
            if (identity == null)
            {
                return this.RedirectToFailureAction(MVC.NewsFeed.Default(CurrentUser.LanguageCode), "SSO failed: identity is null");
            }

            var claimNameIdentifier = identity.FindFirst(ClaimTypes.NameIdentifier);

            if (claimNameIdentifier == null)
            {
                return this.RedirectToFailureAction(MVC.NewsFeed.Default(CurrentUser.LanguageCode), "SSO failed: NameIdentifier is null");
            }

            if (claimNameIdentifier.Issuer == AuthenticationTypes.Lt2Sso)
            {
                if (returnTo.IsNullOrEmpty())
                {
                    returnTo = authentication.DefaultUrl;
                }

                var user = Service.GetUserInfoByUserName(claimNameIdentifier.Value);
                if (user != null)
                {
                    authentication.SignIn(user.UserName, (bool?)TempData["RememberMe"] ?? false);
                }
                else
                {
                    return this.RedirectToFailureAction(MVC.NewsFeed.Default(CurrentUser.LanguageCode), "SSO failed: user not found");
                }

                RedirectUrl = returnTo;

                if (!user.HasSigned)
                {
                    return RedirectToAction(MVC.Account.Manifest());
                }

                if (user.RequireChangePassword)
                {
                    MembershipSession.Reset();
                    return RedirectToAction(MVC.Account.ChangePassword());
                }

                return Redirect(returnTo);
            }

            var email = identity.FindFirst(ClaimTypes.Email);

            if (email == null)
            {
                return this.RedirectToFailureAction(MVC.Account.Login(returnTo), "Email is null");
            }

            var username = identity.FindFirst(ClaimTypes.Name);

            if (username == null)
            {
                username = email;
            }

            var givenNameClaim = identity.FindFirst(ClaimTypes.GivenName);
            var surnameClaim = identity.FindFirst(ClaimTypes.Surname);
            string givenName = null, surname = null;

            if (givenNameClaim == null || surnameClaim == null)
            {
                var name = GetSplitName(identity);
                if (name == null)
                {
                    return this.RedirectToFailureAction(MVC.Account.Login(returnTo), "Name is null");
                }

                givenName = name.Item1;
                surname = name.Item2;
            }
            else
            {
                givenName = givenNameClaim.Value;
                surname = surnameClaim.Value;

            }

            var result = new OAuthLoginModel()
            {
                ReturnTo = returnTo,
                DoRegister = false,
                Email = email.Value,
                FirstName = givenName,
                LastName = surname,
                IsPageLiked = true,
                UserName = username.Value,
                RememberMe = (bool?) TempData["RememberMe"] ?? false
            };

            if (claimNameIdentifier.Issuer == "Facebook")
            {
                var accessToken = identity.FindFirst(ClaimTypes.Sid);
                if (accessToken != null)
                {
                    var client = new FacebookClient(accessToken.Value);
                    result.IsPageLiked = IsFbPageLiked(client);
                }

                result.FacebookId = claimNameIdentifier.Value;
            }

            return OpenAuthRegister(result);
        }

        private Tuple<string, string> GetSplitName(ClaimsIdentity identity)
        {
            var nameClaim = identity.FindFirst(c => c.Type.Contains(":name"));
            if (nameClaim == null)
            {
                Logger.Error("Claim with :name not found.");
                Logger.Information("Got claims: " + string.Join(",", identity.Claims.Select(x => x.Type + ":" + x.Value)));
                return null;
            }

            var name = nameClaim.Value.Trim();

            if (name.IndexOf(' ') < 0)
            {
                Logger.Information("Received name with single word");
                return new Tuple<string, string>(name, "");
            }

            var givenName = name.Substring(0, nameClaim.Value.LastIndexOf(' '));
            var surname = name.Substring(nameClaim.Value.LastIndexOf(' ') + 1);

            return new Tuple<string, string>(givenName, surname);
        }

        [HttpGet, RequireSsl]
        public virtual ActionResult Register(string returnTo)
        {
            if (CurrentUser.IsAuthenticated)
            {
                return RedirectToAction(MVC.Common.Start());
            }

            var model = new UserCreateModel();
            model.MinPasswordLength = CustomAppSettings.MinPasswordLength;
            model.ReturnTo = returnTo;
            if (CurrentUser.IsUnique)
            {
                model.FirstName = CurrentUser.FirstName;
                model.LastName = CurrentUser.LastName;
            }

            return View(MVC.Account.Views.Register, model);
        }

        [HttpPost, RequireSsl, RecaptchaControlMvc.CaptchaValidatorAttribute]
        public virtual ActionResult Register(UserCreateModel model, bool captchaValid)
        {
            if (!captchaValid && !HttpContext.IsDebuggingEnabled)
            {
                ModelState.AddModelError("", Resource.InvalidCaptcha);
            }
            if (model.Password.ToLower().Contains(model.UserName.ToLower()))
            {
                ModelState.AddModelError("", Resource.PasswordSimilarToUserName);
            }

            if (model.Password.ToLower().Contains(model.FirstName.ToLower()) || model.Password.ToLower().Contains(model.LastName.ToLower()))
            {
                ModelState.AddModelError("", Resource.PasswordSimilarToName);
            }

            if (ModelState.IsValid)
            {
                // Attempt to register the user
                var user = Service.Create(model);

                if (user != null)
                {
                    authentication.SignIn(model.UserName);
                    StartTour();

                    RedirectUrl = model.ReturnTo;
                    return RedirectToAction(MVC.Account.Manifest());
                }

                ModelState.AddModelError("", Resource.LoginFailed);
            }

            // If we got this far, something failed, redisplay form
            model.MinPasswordLength = CustomAppSettings.MinPasswordLength;
            return View(MVC.Account.Views.Register, model);
        }

        public virtual ActionResult BeginTour()
        {
            StartTour();
            return RedirectToAction(MVC.Common.Start());
        }

        public virtual ActionResult ValidateUserName(string username)
        {
            return Json(Service.ValidateUserName(username), JsonRequestBehavior.AllowGet);
        }

        public virtual ActionResult ValidateEmail(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                email = Request.QueryString[Request.QueryString.AllKeys.First(p => p.ToLower().Contains("email"))];
            }

            return Json(Service.ValidateEmail(email), JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Logs currently authenticated user out.
        /// </summary>
        /// <returns>The login view.</returns>
        [HttpGet]
        public virtual ActionResult Logout()
        {
            LogLogout(CurrentUser.UserName);
            Service.Logout();
            var redirectUrl = authentication.SingOut();
            Session.Clear();
            MembershipSession.Reset();
            if (string.IsNullOrEmpty(CustomAppSettings.SsoBaseUrl))
            {
                if (IsJsonRequest)
                {
                    return Jsonp(true);
                }

                return Redirect(authentication.DefaultUrl);
            }

            return Redirect(CustomAppSettings.SsoBaseUrl + "/Account/Logout");
        }

        /// <summary>
        /// Displays reminds password view.
        /// </summary>
        /// <returns>The remind password view.</returns>
        [HttpGet, ImportModelStateFromTempData]
        public virtual ActionResult Reset()
        {
            var model = new PasswordResetModel();
            model.MinPasswordLength = CustomAppSettings.MinPasswordLength;
            return View(MVC.Account.Views.Reset, model);
        }

        /// <summary>
        /// Resets user's password, generates new and sends it via email.
        /// </summary>
        /// <param name="model">The view model.</param>
        /// <returns>The login view.</returns>
        [HttpPost, ExportModelStateToTempData]
        public virtual ActionResult Reset(PasswordResetModel model)
        {
            if (ModelState.IsValid)
            {
                if (Service.ResetPassword(model))
                {
                    if (string.IsNullOrEmpty(model.Email))
                    {
                        authentication.SignIn(model.UserName);
                        TempData["SuccessMessage"] = Resource.PasswordResetSuccess;
                        return Redirect("~");
                    }
                    else
                    {
                        TempData["SuccessMessage"] = string.Format(Resource.TempPasswordSent, model.Email);
                        return Redirect("~");
                    }
                }

                ModelState.AddModelError("", "");
            }

            return RedirectToAction(MVC.Account.Reset());
        }

        /// <summary>
        /// Redirects to the default page for the current user.
        /// </summary>
        /// <returns></returns>
        public virtual ActionResult Default()
        {
            return Redirect(authentication.DefaultUrl);
        }

        /// <summary>
        /// Detailses the specified user db id.
        /// </summary>
        /// <param name="userObjectId">The user object id.</param>
        /// <param name="fullName">The full name.</param>
        /// <returns></returns>
        [HttpGet]
        public virtual ActionResult Details(string userObjectId, string fullName, UserViews? view)
        {
            if (userObjectId.IsNullOrEmpty())
            {
                if (CurrentUser.IsAuthenticated)
                {
                    userObjectId = CurrentUser.Id;
                }
                else
                {
                    return RedirectToAction(MVC.Account.Login(Request.RawUrl));
                }
            }

            CommentsFilter = null;

            if (!view.HasValue)
            {
                view = UserViews.Info;
            }

            if (view == UserViews.Settings)
            {
                EnsureCurrentUser(userObjectId);
            }

            try
            {
                var userAccount = Service.GetUserAccount(userObjectId, view.Value);

                string expectedName = userAccount.FullName.ToSeoUrl();
                string actualName = (fullName ?? "").ToLower();

                // permanently redirect to the correct URL
                if (expectedName != actualName)
                {
                    return RedirectToActionPermanent("Details", "Account",
                                                     new
                                                         {
                                                             userObjectId = userObjectId,
                                                             fullName = expectedName,
                                                             view = view
                                                         });
                }

                if (IsTour)
                {
                    Tour(MVC.Idea.Index(),
                         Resource.TourAccount);
                }

                return View(MVC.Account.Views.Details, userAccount);
            }
            catch (ObjectNotFoundException ex)
            {
                return HttpNotFound();
            }
            catch (SecurityException ex)
            {
                return HttpNotFound();
            }
        }

        public virtual ActionResult FinishTheTour()
        {
            base.FinishTour();
            if (Request.IsAjaxRequest())
            {
                return Json(true);
            }

            return RedirectToAction(MVC.Common.Start());
        }

        public virtual ActionResult GetUserInfo(string userObjectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            var userAccount = Service.GetUserAccount(userObjectId, UserViews.Info);
            return Json(new { Content = RenderPartialViewToString(MVC.Account.Views.UserInfo, userAccount) });
        }

        public virtual ActionResult PersonalInfo(string userObjectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            var personalInfo = Service.GetPersonalInfo(userObjectId);
            return Json(RenderPartialViewToString(MVC.Account.Views.PersonalInfo, personalInfo));
        }

        [Authorize]
        public virtual ActionResult SavePersonalInfo(PersonalInfoEditModel personalInfo)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(personalInfo.UserObjectId, null, null));
            }

            if (!ModelState.IsValid)
            {
                var info = Service.GetPersonalInfoForEdit(personalInfo);
                return Json(new
                {
                    Content = RenderPartialViewToString(MVC.Account.Views.PersonalInfoEdit, info),
                    IsValid = ModelState.IsValid
                });
            }

            EnsureCurrentUser(personalInfo.UserObjectId);
            var personalInfoView = Service.SavePersonalInfo(personalInfo);
            return Json(new
            {
                Content = RenderPartialViewToString(MVC.Account.Views.PersonalInfo, personalInfoView),
                IsValid = ModelState.IsValid
            });
        }

        public virtual ActionResult Interests(string userObjectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            var view = Service.GetInterests(userObjectId);
            return Json(RenderPartialViewToString(MVC.Account.Views.Interests, view));
        }

        public virtual ActionResult Contacts(string userObjectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            var view = Service.GetContacts(userObjectId);
            return Json(RenderPartialViewToString(MVC.Account.Views.Contacts, view));
        }

        [Authorize]
        public virtual ActionResult SaveInterests(InterestsEditModel interestsModel)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(interestsModel.UserObjectId, null, null));
            }

            if (!ModelState.IsValid)
            {
                return Json(new
                {
                    Content = RenderPartialViewToString(MVC.Account.Views.InterestsEdit, interestsModel),
                    IsValid = ModelState.IsValid
                });
            }

            EnsureCurrentUser(interestsModel.UserObjectId);
            var view = Service.SaveInterests(interestsModel);
            return Json(new
            {
                Content = RenderPartialViewToString(MVC.Account.Views.Interests, view),
                IsValid = ModelState.IsValid
            });
        }

        [Framework.Mvc.Filters.Authorize]
        public virtual ActionResult SaveContacts(ContactsEditModel model)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(model.UserObjectId, null, null));
            }

            EnsureCurrentUser(model.UserObjectId);

            if (!Service.ValidateMunicipality(model.Country, model.Municipality))
            {
                ModelState.AddModelError("Municipality", Resource.SelectExistingMunicipality);
            }

            if (!Service.ValidateMunicipality(model.OriginCountry, model.OriginMunicipality))
            {
                ModelState.AddModelError("OriginMunicipality", Resource.SelectExistingMunicipality);
            }

            if (!Service.HasConfirmedEmails(model))
            {
                ModelState.AddModelError("Emails", Resource.ConfirmedEmailRequired);
            }

            if (!ModelState.IsValid)
            {
                model = Service.FillContactsModel(model);
                return Json(new
                {
                    Content = RenderPartialViewToString(MVC.Account.Views.ContactsEdit, model),
                    IsValid = ModelState.IsValid
                });
            }

            var view = Service.SaveContacts(model);

            return Json(new
            {
                Content = RenderPartialViewToString(MVC.Account.Views.Contacts, view),
                IsValid = ModelState.IsValid
            });
        }

        [Authorize]
        public virtual ActionResult EditPersonalInfo(string userObjectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            EnsureCurrentUser(userObjectId);
            var personalInfo = Service.GetPersonalInfoForEdit(userObjectId);
            return Json(new
            {
                Content = RenderPartialViewToString(MVC.Account.Views.PersonalInfoEdit, personalInfo),
                IsValid = ModelState.IsValid
            });
        }

        [Authorize]
        public virtual ActionResult EditInterests(string userObjectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            EnsureCurrentUser(userObjectId);
            var model = Service.GetInterestsForEdit(userObjectId);
            return Json(new
            {
                Content = RenderPartialViewToString(MVC.Account.Views.InterestsEdit, model),
                IsValid = ModelState.IsValid
            });
        }

        [Framework.Mvc.Filters.Authorize]
        public virtual ActionResult EditContacts(string userObjectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            EnsureCurrentUser(userObjectId);
            var model = Service.GetContactsForEdit(userObjectId);
            return Json(new
            {
                Content = RenderPartialViewToString(MVC.Account.Views.ContactsEdit, model),
                IsValid = ModelState.IsValid
            });
        }

        [Authorize]
        public virtual ActionResult EditEducationAndWork(string userObjectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            EnsureCurrentUser(userObjectId);
            var model = Service.GetEducationAndWorkForEdit(userObjectId);
            return Json(new
            {
                Content = RenderPartialViewToString(MVC.Account.Views.EducationAndWorkEdit, model),
                IsValid = ModelState.IsValid
            });
        }

        public virtual ActionResult EducationAndWork(string userObjectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            EnsureCurrentUser(userObjectId);
            var model = Service.GetEducationAndWork(userObjectId);
            return Json(RenderPartialViewToString(MVC.Account.Views.EducationAndWork, model));
        }

        [Authorize]
        public virtual ActionResult SaveEducationAndWork(EducationAndWorkEditModel editModel)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(editModel.UserObjectId, null, null));
            }

            if (!ModelState.IsValid)
            {
                Service.AssignEducationAndWorkSelectLists(editModel);
                return Json(new
                {
                    Content = RenderPartialViewToString(MVC.Account.Views.EducationAndWorkEdit, editModel),
                    IsValid = ModelState.IsValid
                });
            }
            EnsureCurrentUser(editModel.UserObjectId);
            var model = Service.SaveEducationAndWork(editModel);
            return Json(new { Content = RenderPartialViewToString(MVC.Account.Views.EducationAndWork, model), IsValid = ModelState.IsValid });
        }

        [Authorize]
        public virtual ActionResult AddEducation(int index)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(null, null, null));
            }

            var model = new EducationAndWorkEditModel();
            model.Years = Service.GetEducationYears().ToSelectList();
            for (var i = 0; i <= index; i++)
            {
                model.Educations.Add(new EducationEditModel());
            }
            model.EditIndex = index;
            return Json(new { Content = RenderPartialViewToString(MVC.Account.Views.Education, model), UpdatedHref = Url.Action(MVC.Account.AddEducation(index + 1)) });
        }

        [Authorize]
        public virtual ActionResult AddPosition(int index)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(null, null, null));
            }

            var model = new EducationAndWorkEditModel();
            Service.AssignEducationAndWorkSelectLists(model);
            for (var i = 0; i <= index; i++)
            {
                model.Positions.Add(new PositionEditModel());
            }
            model.EditIndex = index;
            return Json(new { Content = RenderPartialViewToString(MVC.Account.Views.Position, model), UpdatedHref = Url.Action(MVC.Account.AddPosition(index + 1)) });
        }

        [Authorize]
        public virtual ActionResult AddParty(int index)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(null, null, null));
            }

            var model = new EducationAndWorkEditModel();
            Service.AssignEducationAndWorkSelectLists(model);
            for (var i = 0; i <= index; i++)
            {
                model.MemberOfParties.Add(new MemberOfPartiesEditModel());
            }
            model.EditIndex = index;
            return Json(new { Content = RenderPartialViewToString(MVC.Account.Views.MemberOfParty, model), UpdatedHref = Url.Action(MVC.Account.AddParty(index + 1)) });
        }

        [Authorize]
        public virtual ActionResult LikeUser(string userObjectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            Service.LikeUser(userObjectId);
            return Json(string.Empty);
        }

        [Authorize]
        public virtual ActionResult UnlikeUser(string userObjectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            Service.UnlikeUser(userObjectId);
            return Json(string.Empty);
        }

        [HttpPost]
        public virtual ActionResult SaveProfilePicture(string userObjectId, HttpPostedFileBase hpf)
        {
            if (hpf.ContentLength == 0)
                return Details(userObjectId, null, UserViews.Activity);

            BinaryReader b = new BinaryReader(hpf.InputStream);
            byte[] fileBytes = b.ReadBytes((int)hpf.ContentLength);

            Service.SaveProfilePicture(userObjectId, fileBytes, hpf.ContentType);

            return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
        }


        public virtual ActionResult GetUsersThatLikeMe(string userObjectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            var model = Service.GetUsersThatLikeMe(userObjectId, 0);
            model.ActionResult = MVC.Account.GetNextUsersThatLikeMePage(userObjectId, null);
            var json =
                new { Content = RenderPartialViewToString(MVC.Shared.Views.SimpleListContainer, model) };
            return Json(json);
        }

        public virtual ActionResult GetNextUsersThatLikeMePage(string userObjectId, int? pageIndex)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            if (!pageIndex.HasValue)
            {
                return Json(null);
            }

            var issues = Service.GetUsersThatLikeMe(userObjectId, pageIndex.Value);

            var json =
                new { Content = RenderPartialViewToString(MVC.Shared.Views.SimpleList, issues.List.List), issues.List.HasMoreElements };
            return Json(json);
        }

        public virtual ActionResult GetMyPointsPerCategory(string userObjectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            var model = Service.GetUserPointsPerCategory(userObjectId);

            var json =
                new { Content = RenderPartialViewToString(MVC.Account.Views.PointsPerCategory, model) };
            return Json(json);
        }

        public virtual ActionResult ConfirmEmail(string code, string email)
        {
            if (Service.ConfirmEmail(code, email))
            {
                return View(MVC.Account.Views.EmailConfirmed);
            }

            return View();
        }

        public virtual ActionResult SendEmailConfirmation(string email)
        {
            var result = Service.SendEmailConfirmation(email);

            return Json(result);
        }

        [LinkedInAuthorize, Authorize]
        public virtual ActionResult ImportProfileFromLinkedin(bool? overwrite)
        {
            LinkedInService service = new LinkedInService(ViewBag.LinkedInAuthorization);
            List<ProfileField> fields = new List<ProfileField>();
            fields.Add(ProfileField.FirstName);
            fields.Add(ProfileField.LastName);
            fields.Add(ProfileField.PositionId);
            fields.Add(ProfileField.PositionTitle);
            fields.Add(ProfileField.PositionSummary);
            fields.Add(ProfileField.PositionStartDate);
            fields.Add(ProfileField.PositionEndDate);
            fields.Add(ProfileField.PositionIsCurrent);
            fields.Add(ProfileField.PositionCompanyName);
            fields.Add(ProfileField.EducationSchoolName);
            fields.Add(ProfileField.EducationActivities);
            fields.Add(ProfileField.EducationEndDate);
            fields.Add(ProfileField.EducationFieldOfStudy);
            fields.Add(ProfileField.EducationStartDate);
            fields.Add(ProfileField.DateOfBirth);
            fields.Add(ProfileField.MainAddress);
            fields.Add(ProfileField.Associations);
            fields.Add(ProfileField.Honors);
            fields.Add(ProfileField.Interests);
            fields.Add(ProfileField.MemberUrlUrl);
            fields.Add(ProfileField.MemberUrlName);
            fields.Add(ProfileField.LocationCountryCode);
            fields.Add(ProfileField.PictureUrl);
            fields.Add(ProfileField.Summary);
            fields.Add(ProfileField.Specialties);

            var user = service.GetCurrentUser(ProfileType.Standard, fields);
            Service.ImportProfileInfo(user, RemoteController.GetHtmlData(user.PictureUrl), overwrite ?? false);
            return RedirectToSuccessAction(MVC.Account.Details(CurrentUser.Id, CurrentUser.FullName.ToSeoUrl(), UserViews.Info), Globalization.Resources.Account.Resource.ImportSuccessful);
        }

        public virtual ActionResult DeleteUserRequest()
        {
            if (!CurrentUser.IsAuthenticated)
            {
                throw new Exception("Illegal attempt to delete user");
            }

            return View();
        }

        [HttpPost]
        public virtual ActionResult DeleteUserRequest(UserDeleteRequestViewModel model)
        {
            if (!CurrentUser.IsAuthenticated)
            {
                throw new Exception("Illegal attempt to delete user");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    Service.SendUserDeleteMessage(model);

                    Service.DeleteUser(CurrentUser.Id);

                    Service.LogUserActivity(CurrentUser.UserName,
                        string.Format("Account deleted. Reason: {0}. Comment: {1}", model.Reason, model.Comment),
                        Request.UserHostAddress);

                    Logout();

                    return RedirectToSuccessAction(MVC.Common.Start(), Resource.UserDeleteSuccess);
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", Resource.UserDeleteFailed);
                    ErrorSignal.FromCurrentContext().Raise(ex);
                }

            }

            return View();
        }

        [Authorize]
        public virtual ActionResult DeleteUser(string userObjectId)
        {
            if (!CurrentUser.IsAuthenticated || CurrentUser.Role != UserRoles.Admin)
            {
                throw new Exception("Illegal attempt to delete user by user " + CurrentUser.UserName);
            }

            Service.DeleteUser(userObjectId);

            return RedirectToAction(MVC.Common.Start());
        }

        public virtual ActionResult SendMessage(string messageToUserObjectId, string mailMessage, string addressFrom, string subject)
        {
            return Json(Service.SendMessage(messageToUserObjectId, mailMessage, addressFrom, subject));
        }

        public virtual ActionResult GetLanguages(string returnTo)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Common.Start());
            }

            var model = Service.GetLanguages();
            ViewBag.ReturnTo = returnTo;
            return Json(new { Content = RenderPartialViewToString(MVC.Account.Views.Languages, model) });
        }

        public virtual ActionResult ChangeLanguage(string code, string returnTo)
        {
            
            var model = Service.ChangeUserLanguage(code);

            if (string.IsNullOrEmpty(returnTo) || returnTo.EndsWith("/lt") || returnTo.EndsWith("/en") || returnTo.Contains("kok-IN"))
            {
                return RedirectToAction(MVC.Common.Start());
            }

            return Redirect(returnTo);
        }

        public virtual ActionResult SuggestUser(string prefix)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Common.Start());
            }

            var result = Service.GetUsers(prefix);
            return Json(result);
        }

        public virtual ActionResult LoginPopup(string returnUrl)
        {
            if (!string.IsNullOrEmpty(CustomAppSettings.SsoBaseUrl))
            {
                return SsoChallenge(returnUrl);
            }

            var model = new UserLoginModel()
            {
                ReturnTo = returnUrl,
                ExternalAuthenticationTypes = this.authenticationService.GetExternalAuthenticationTypes()
            };
            return Json(new { Content = RenderPartialViewToString(MVC.Account.Views.LoginPartial, model) });
        }

        public virtual ActionResult GetConfirmPersonCodeForm(string returnUrl)
        {
            return Json(new
            {
                Content = RenderPartialViewToString(
                    MVC.Account.Views.ConfirmPersonCode,
                    new ConfirmPersonCodeModel() { ReturnUrl = returnUrl })
            });
        }

        public virtual ActionResult ConfirmPersonCode(ConfirmPersonCodeModel model)
        {
            if (model.PersonCode != CurrentUser.PersonCode)
            {
                ModelState.AddModelError("", Resource.InvalidPersonCode);
            }

            if (ModelState.IsValid)
            {
                CurrentUser.IsConfirmedThisSession = true;
                if (IsAjaxRequest)
                {
                    return Json(new {success = true, url = model.ReturnUrl});
                }

                return Redirect(model.ReturnUrl);
            }

            if (IsAjaxRequest)
            {
                return Json(new {Content = RenderPartialViewToString(MVC.Account.Views.ConfirmPersonCode, model)});
            }

            return Redirect(model.ReturnUrl);
        }

        public virtual ActionResult MergeUsers(string userFromObjectId, string userToObjectId)
        {
            if (CurrentUser.Role != UserRoles.Admin)
            {
                throw new UnauthorizedAccessException();
            }

            if (Service.MergeUsers(userFromObjectId, userToObjectId))
            {
                return RedirectToSuccessAction(MVC.Common.Start(),
                                               Resource.MergeSuccess);
            }

            return RedirectToSuccessAction(MVC.Common.Start(),
                                               Resource.MergeFailed);
        }

        [Authorize]
        public virtual ActionResult AwardUser(string userObjectId, UserAwards award)
        {
            if (CurrentUser.Role != UserRoles.Admin)
            {
                ModelState.AddModelError("", Resource.AwardFailed);
                return Details(userObjectId, null, null);
            }

            if (Service.AwardUser(userObjectId, award))
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            ModelState.AddModelError("", Resource.AwardFailed);
            return Details(userObjectId, null, null);
        }

        [Authorize]
        public virtual ActionResult TakeBackAward(string userObjectId, UserAwards award)
        {
            if (CurrentUser.Role != UserRoles.Admin)
            {
                ModelState.AddModelError("", Resource.TakeBackAwardFailed);
                return Details(userObjectId, null, null);
            }

            if (Service.TakeBackAward(userObjectId, award))
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            ModelState.AddModelError("", Resource.TakeBackAwardFailed);
            return Details(userObjectId, null, null);
        }

        public virtual ActionResult RequireUniqueAuthentication(string userObjectId, bool require)
        {
            if (CurrentUser.Role != UserRoles.Admin)
            {
                ModelState.AddModelError("", Resource.NoRights);
                return Details(userObjectId, null, null);
            }

            Service.RequireUniqueAuthentication(userObjectId, require);
            return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
        }

        [HttpGet]
        [Authorize]
        public virtual ActionResult Manifest(string act = "")
        {
            if (act == "resend")
            {
                Service.SendEmailConfirmation();
                return RedirectToAction(MVC.Account.Manifest());
            }

            if (IsTour)
            {
                if (string.IsNullOrEmpty(CurrentUser.Email))
                {
                    Tour(null,
                         Resource.TourManifest);
                }
            }
            return View();
        }

        [HttpPost]
        [Authorize]
        public virtual ActionResult Manifest(bool? sign)
        {
            if (!Service.IsEmailConfirmed())
            {
                ModelState.AddModelError("",
                    string.Format(Resource.ConfirmEmail, Url.Action(MVC.Account.Manifest("resend"))));
                return View();
            }
            Service.SignManifest();

            if (!string.IsNullOrEmpty(RedirectUrl))
            {
                return Redirect(AddSsoValuesToUrl(RedirectUrl));
            }

            if (IsTour)
            {
                return RedirectToAction(MVC.Voting.EditMyCategories());
            }

            return RedirectToAction(MVC.Common.Start());
        }

        [HttpGet]
        [Authorize]
        public virtual ActionResult Ambasador()
        {
            return View();
        }

        [Authorize]
        public virtual ActionResult Ambasador(bool? sign)
        {
            Service.BecomeAmbasador();

            TempData["Ambasador"] = true;

            return RedirectToAction(MVC.Account.Details(CurrentUser.Id, CurrentUser.FullName.ToSeoUrl(), null));
        }

        [Authorize]
        public virtual ActionResult CancelAmbasador(bool? sign)
        {
            Service.CancelAmbasador();

            return RedirectToAction(MVC.Account.Details(CurrentUser.Id, CurrentUser.FullName.ToSeoUrl(), null));
        }

        public virtual ActionResult SetFacebookPermissionGranted(bool isGranted)
        {
            Service.SetFacebookPermissionGranted(isGranted);
            return Json(true);
        }

        public virtual ActionResult SetFacebookPageLiked(bool liked)
        {
            Service.SetFacebookPageLiked(CurrentUser.DbId, liked);
            return Json(true);
        }

        public virtual ActionResult SetPostedToFacebook()
        {
            CurrentUser.PostedToFacebookDate = DateTime.Now;
            Service.SetFacebookPermissionGranted(true);
            return Json(true);
        }

        public virtual ActionResult SetFacebookStatus(long? facebookId, bool isConnected)
        {
            CurrentUser.ConnectedFacebookId = facebookId;
            CurrentUser.IsConnectedToFacebook = isConnected;
            return Json(true);
        }

        public virtual ActionResult SetFacebookId(long? facebookId, string email, bool replace = false)
        {
            if (facebookId.HasValue)
            {
                var result = Service.SaveFacebookId(facebookId.Value, replace);
                if (result == null)
                {
                    CurrentUser.FacebookId = facebookId;
                    CurrentUser.ConnectedFacebookId = facebookId;
                }
                else
                {
                    return Json(new { error = result });
                }

                if (string.IsNullOrEmpty(CurrentUser.Email))
                {
                    Service.SaveFacebookUserEmail(email);
                    CurrentUser.Email = email;
                }

                Service.SaveFacebookPicture(CurrentUser);

                return Json(new { success = true });
            }

            return Json(new { success = false });
        }

        public virtual ActionResult BankLink(AuthenticationSources? bank)
        {
            if (!bank.HasValue)
            {
                return RedirectToAction(MVC.Common.Start());
            }

            if (Request.UrlReferrer != null && !Request.UrlReferrer.AbsoluteUri.Contains("/login"))
            {
                LastReferrerUrl = Request.UrlReferrer;
            }

            try
            {
                var model = Service.GetBankLinkModel(bank.Value, false);
                if (model.Contacts != null)
                {
                    return
                        Json(new { Contacts = RenderPartialViewToString(MVC.Account.Views.BankContacts, model.Contacts) });
                }
                return Json(new { Content = RenderPartialViewToString(MVC.Account.Views.BankLink, model) });
            }
            catch (Exception ex)
            {
                var context = System.Web.HttpContext.Current;
                ErrorSignal.FromContext(context).Raise(ex, context);
                return RedirectToFailureAction(MVC.Account.Login(), Resource.BankLinkFailed);
            }
        }

        public virtual ActionResult BankLinkSuccess(AuthenticationSources bank)
        {
            var result = Service.VerifyBankResponse(Request.QueryString, Request.Form, bank);
            if (!result)
            {
                return RedirectToFailureAction(MVC.NewsFeed.Default(CurrentUser.LanguageCode), Resource.ConfirmIdentityFailed);
            }
            var username = Service.UniqueUser(Request.QueryString, Request.Form, bank);

            if (username != null)
            {
                authentication.SignIn(username, false);
                LogLogin(username, true);
            }

            if (LastVoteUrl != null)
            {
                return Redirect(LastVoteUrl.PathAndQuery);
            }

            if (LastReferrerUrl != null)
            {
                return Redirect(LastReferrerUrl.PathAndQuery);
            }

            return RedirectToAction(MVC.Common.Start());
        }

        public virtual ActionResult InitViispAuth()
        {
            if (Request.UrlReferrer != null && !Request.UrlReferrer.AbsoluteUri.Contains("/login"))
            {
                LastReferrerUrl = Request.UrlReferrer;
            }

            try
            {
                using (var viisp = new VIISPServiceOperations())
                {
                    var ticket = viisp.GetAuthTicket();
                    var model = Service.GetBankLinkModel(ticket);

                    LogLogout(CurrentUser.UserName);
                    Service.Logout();
                    var redirectUrl = authentication.SingOut();
                    MembershipSession.Reset();

                    if (IsAjaxRequest)
                    {
                        return Json(new {Content = RenderPartialViewToString(MVC.Account.Views.BankLink, model)});
                    }

                    return View(MVC.Account.Views.BankLink, model);
                }
                
            }
            catch (Exception ex)
            {
                var context = System.Web.HttpContext.Current;
                ErrorSignal.FromContext(context).Raise(ex, context);
                return RedirectToFailureAction(MVC.Account.Login(), Resource.BankLinkFailed);
            }

            //var data = Services.VIISP.VIISPServiceOperations.GetAuthData(ticket);
            //return Content(data.ToString());
        }

        public virtual ActionResult ViispAuth(string ticket, string customData)
        {
            if (string.IsNullOrEmpty(ticket))
            {
                goto Finish;
            }

            using (var viisp = new VIISPServiceOperations())
            {
                var data = viisp.GetAuthData(ticket);

                Service.UniqueUser(data.authenticationDataResponse);
            }

            if (LastVoteUrl != null)
            {
                return Redirect(LastVoteUrl.PathAndQuery);
            }

            Finish:
            if (LastReferrerUrl != null)
            {
                return Redirect(LastReferrerUrl.PathAndQuery);
            }

            return RedirectToAction(MVC.Common.Start());
        }

        public virtual ActionResult BankLinkCancel(AuthenticationSources bank)
        {
            return RedirectToFailureAction(MVC.NewsFeed.Default(CurrentUser.LanguageCode), Resource.BanklinkCanceled, true);
        }

        public virtual ActionResult BankLinkReject(AuthenticationSources bank)
        {
            return RedirectToFailureAction(MVC.NewsFeed.Default(CurrentUser.LanguageCode), Resource.BankLinkReject, true);
        }

        public virtual ActionResult Unsubscribe(string email, string sign)
        {
            if (!string.IsNullOrEmpty(email))
            {
                var id = Service.Unsubscribe(email, sign);
                if (id != null)
                {
                    return RedirectToSuccessAction(MVC.Account.Details(id, null, null),
                                                   Resource.UnsubscribeSuccess);
                }
            }

            return RedirectToSuccessAction(MVC.NewsFeed.Default(CurrentUser.LanguageCode), string.Format("El. paštas {0} nerastas", email));
        }

        private void EnsureCurrentUser(string userObjectId)
        {
            if (userObjectId != CurrentUser.Id)
            {
                var currentUserName = CurrentUser.FullName;

                throw new UnauthorizedAccessException(string.Format("UnauthorizedAccessException: You don't have access. You're currently logged in as {0}.",
                                                      currentUserName));
            }
        }

        /// <summary>
        /// Validates the specified model.
        /// </summary>
        /// <param name="model">The model to validate.</param>
        private UserInfo Validate(UserLoginModel model)
        {
            if (!String.IsNullOrEmpty(model.UserName) && !String.IsNullOrEmpty(model.Password))
            {
                var user = Service.Login(model.UserName, model.Password);
                if (user == null)
                {
                    ModelState.AddModelError("password", Resource.InvalidCredentials);
                }

                LogLogin(model.UserName, user != null);
                return user;
            }

            return null;
        }

        /// <summary>
        /// Logs authentication information.
        /// </summary>
        /// <param name="username">The user name.</param>
        /// <param name="succeeded">If set to <c>true</c>, authentication succeeded.</param>
        private void LogLogin(string username, bool succeeded)
        {
            var ip = Request.ServerVariables["REMOTE_ADDR"];
            var message = string.Empty;
            message = succeeded ? "Logged in" : "Failed log in";

            Service.LogUserActivity(username, message, ip);
        }

        /// <summary>
        /// Logs log out information.
        /// </summary>
        /// <param name="username">The user name.</param>
        private void LogLogout(string username)
        {
            var ip = Request.ServerVariables["REMOTE_ADDR"];
            var message = "Logged out";

            Service.LogUserActivity(username, message, ip);
        }

        [ExportModelStateToTempData]
        public virtual ActionResult SaveAdditionalUniqueInfo(AdditionalUniqueInfoModel model)
        {
            if (!model.AcceptTerms)
            {
                ModelState.AddModelError("AcceptTerms", Resource.AcceptTerms);
            }

            if (model.DocumentNo == CurrentUser.PersonCode)
            {
                ModelState.AddModelError("DocumentNo", Resource.DocumentNoNotValid);
            }

            if (!Service.ValidateMunicipality(model.Country, model.Municipality))
            {
                ModelState.AddModelError("Municipality", Resource.SelectExistingMunicipality);
            }

            if (ModelState.IsValid)
            {
                Service.SaveAdditionalUniqueInfo(model);
                if (LastVoteUrl != null)
                {
                    return Redirect(LastVoteUrl.PathAndQuery);
                }
            }

            TempData["OpenAdditinalUniqueInfoDialog"] = model;
            if (LastReferrerUrl != null)
            {
                return Redirect(LastReferrerUrl.PathAndQuery);
            }

            return RedirectToAction(MVC.NewsFeed.Default(CurrentUser.LanguageCode));
        }

        public virtual ActionResult ConfirmIdentity()
        {
            Service.GetAdditionalVotingInfo(CurrentUser.PersonCode);
            CurrentUser.AuthenticationSource = AuthenticationSources.Lt2.ToString();
            return Jsonp(
                new
                {
                    Error = typeof(AdditionalUniqueInfoRequiredException).Name,
                    CurrentUser.FirstName,
                    CurrentUser.LastName,
                    CurrentUser.PersonCode,
                    CurrentUser.AdditionalInfo.AddressLine,
                    CurrentUser.AdditionalInfo.DocumentNo,
                    CurrentUser.AdditionalInfo.City,
                    CurrentUser.AdditionalInfo.Municipality,
                    CurrentUser.AdditionalInfo.Country,
                    DocumentNoRequired = true
                },
                JsonRequestBehavior.AllowGet
            );
        }

        public virtual ActionResult Settings(string userObjectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            EnsureCurrentUser(userObjectId);

            var userAccount = Service.GetUserSettings(userObjectId);
            return Json(new { Content = RenderPartialViewToString(MVC.Account.Views.Settings, userAccount) });
        }

        [HttpPost]
        public virtual ActionResult SaveSettings(Data.ViewModels.Account.SettingsModel model)
        {
            if (ModelState.IsValid)
            {
                Service.SaveSettings(model);
                return
                    RedirectToSuccessAction(
                        MVC.Account.Details(model.UserObjectId, null, UserViews.Settings),
                        SharedStrings.SaveSuccess);
            }

            return Details(model.UserObjectId, null, UserViews.Settings);
        }

        #region comments
        [Authorize]
        public virtual ActionResult AddComment(CommentView model, EmbedModel embed)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    var comment = Service.AddNewComment(model.EntryId, model.CommentText, embed, model.ForAgainst,
                                                        model.VersionId);
                    if (IsJsonRequest)
                    {
                        return Jsonp(comment);
                    }

                    if (!IsAjaxRequest)
                    {
                        return RedirectToAction(MVC.Account.Details(model.EntryId, null, UserViews.Comments));
                    }
                    return Jsonp(new { Comment = RenderPartialViewToString(MVC.Comments.Views._Comment, comment) });
                }
                catch (Exception ex)
                {
                    return ProcessError(ex);
                }
            }

            if (!IsAjaxRequest)
            {
                return RedirectToAction(MVC.Account.Details(model.EntryId, null, UserViews.Comments));
            }
            return Jsonp(false);
        }

        [Authorize]
        public virtual ActionResult AddCommentComment(CommentView model, EmbedModel embed)
        {
            if (!IsAjaxRequest)
            {
                return RedirectToAction(MVC.Account.Details(model.EntryId, null, null));
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var comment = Service.AddNewCommentToComment(model.EntryId, model.Id, model.CommentText, embed);

                    if (IsJsonRequest)
                    {
                        return Jsonp(comment);
                    }

                    return Jsonp(new { Comment = RenderPartialViewToString(MVC.Comments.Views._CommentComment, comment) });
                }
                catch (Exception ex)
                {
                    return ProcessError(ex);
                }
            }

            return Jsonp(false);
        }

        [Authorize]
        public virtual ActionResult DeleteComment(string id, string commentId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(id, null, null));
            }

            var success = Service.DeleteComment(id, commentId);
            return Json(success);
        }

        [Authorize]
        public virtual ActionResult DeleteCommentComment(string id, string commentId, string commentCommentId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(id, null, null));
            }

            var success = Service.DeleteCommentComment(id, commentId, commentCommentId);
            return Json(success);
        }

        public virtual ActionResult GetComments(string id, CommentViews? sort, ForAgainst? filter)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(id, null, null));
            }

            if (!sort.HasValue && !filter.HasValue)
            {
                CommentsFilter = null;
            }

            sort = GetCommentSort(sort);
            filter = GetCommenFilter(filter);

            ExpandableList<CommentView> comments = null;

            if (sort == CommentViews.MostSupported)
            {
                comments = Service.GetCommentsMostSupported(id, 0, filter);
            }
            else
            {
                comments = Service.GetCommentsMostRecent(id, 0, filter);
            }

            var json =
                new
                {
                    Content = RenderPartialViewToString(MVC.Comments.Views._CommentList, comments.List),
                    comments.HasMoreElements,
                    UpdatedHref = Url.Action(MVC.Account.GetMoreComments(id, null, sort, filter))
                };
            return Json(json);
        }

        public virtual ActionResult GetCommentsModel(string id)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(id, null, null));
            }

            var model = Service.GetCommentsModel(id);

            return Json(new
                            {
                                Content = RenderPartialViewToString(MVC.Comments.Views.TypedComments, model)
                            });
        }

        public virtual ActionResult GetMoreComments(string id, int? pageIndex, CommentViews? sort, ForAgainst? filter)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(id, null, null));
            }

            if (!pageIndex.HasValue)
            {
                return Json(null);
            }

            ExpandableList<CommentView> comments = null;

            if (!sort.HasValue || sort == CommentViews.MostSupported)
            {
                comments = Service.GetCommentsMostSupported(id, pageIndex.Value, filter);
            }
            else
            {
                comments = Service.GetCommentsMostRecent(id, pageIndex.Value, filter);
            }

            var json =
                new
                {
                    Content = RenderPartialViewToString(MVC.Comments.Views._CommentList, comments.List),
                    comments.HasMoreElements
                };
            return Json(json);
        }

        [Authorize]
        public virtual ActionResult LikeComment(string id, string commentId, string parentId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(id, null, null));
            }

            var result = Service.LikeComment(id, commentId, parentId);
            return Json(new { Content = RenderPartialViewToString(MVC.Comments.Views.Like, result) });
        }

        [Authorize]
        public virtual ActionResult UndoLikeComment(string id, string commentId, string parentId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(id, null, null));
            }

            var result = Service.UndoLikeComment(id, commentId, parentId);
            return Json(new { Content = RenderPartialViewToString(MVC.Comments.Views.Like, result) });
        }

        [Authorize]
        public virtual ActionResult HideComment(string id, string commentId, string parentId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(id, null, null));
            }

            var result = Service.HideComment(id, commentId, parentId);
            return Json(new { Content = RenderPartialViewToString(string.IsNullOrEmpty(parentId) ? MVC.Comments.Views._Comment : MVC.Comments.Views._CommentComment, result) });
        }

        [Authorize]
        public virtual ActionResult ShowComment(string id, string commentId, string parentId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(id, null, null));
            }

            var result = Service.ShowComment(id, commentId, parentId);
            return Json(new { Content = RenderPartialViewToString(string.IsNullOrEmpty(parentId) ? MVC.Comments.Views._Comment : MVC.Comments.Views._CommentComment, result) });
        }

        private CommentViews GetCommentSort(CommentViews? sort)
        {
            if (sort.HasValue)
            {
                CommentsSort = sort.Value;
                return sort.Value;
            }

            if (CommentsSort.HasValue)
            {
                return CommentsSort.Value;
            }

            return CommentViews.MostSupported;
        }

        private ForAgainst? GetCommenFilter(ForAgainst? filter)
        {
            if (filter.HasValue)
            {
                CommentsFilter = filter.Value;
                return filter.Value;
            }

            if (CommentsFilter.HasValue)
            {
                return CommentsFilter.Value;
            }

            return null;
        }

        #endregion

        public virtual ActionResult BlockUser(string userObjectId)
        {
            Service.BlockUser(userObjectId);
            return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
        }

        public virtual ActionResult UnblockUser(string userObjectId)
        {
            try
            {
                Service.UnblockUser(userObjectId);
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }
            catch (Exception ex)
            {
                return ProcessError(ex);
            }
        }



        [Framework.Mvc.Filters.Authorize]
        public virtual ActionResult AddPhoneNumber(string listName = "PhoneNumbers")
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                if (Request.UrlReferrer != null)
                {
                    return Redirect(Request.UrlReferrer.ToString());
                }
            }

            var model = new PhoneNumberEditModel(listName);

            return Json(new { Content = RenderPartialViewToString(MVC.Account.Views.PhoneNumber, model) });
        }

        [Framework.Mvc.Filters.Authorize]
        public virtual ActionResult AddEmail()
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                if (Request.UrlReferrer != null)
                {
                    return Redirect(Request.UrlReferrer.ToString());
                }
            }

            var model = new EmailModel();

            return Json(new { Content = RenderPartialViewToString(MVC.Account.Views._Email, model) });
        }

        public virtual ActionResult GetAuthenticatedUserInfo()
        {
            return Jsonp(GetSsoUserInfo());
        }

        private string GetSsoUserInfo()
        {
            return CurrentUser.IsAuthenticated
                ? SecurityUtil.Encrypt(CurrentUser.UserName + ";" + DateTime.Now, CustomAppSettings.SsoSharedSecret)
                : string.Empty;
        }

        public virtual ActionResult ShowTutorial()
        {
            Service.ShowTutorial();
            return Json(new { Content = RenderPartialViewToString(MVC.Account.Views._Tutorial) });
        }

        public virtual ActionResult Politician(string userObjectId, bool isPolitician)
        {
            if (CurrentUser.Role != UserRoles.Admin)
            {
                ModelState.AddModelError("", Resource.AwardFailed);
                return Details(userObjectId, null, null);
            }

            if (Service.Politician(userObjectId, isPolitician))
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            ModelState.AddModelError("", Resource.AwardFailed);
            return Details(userObjectId, null, null);
        }

        public virtual ActionResult UpdateDb()
        {
            Service.UpdateDb();
            return RedirectToAction(MVC.Common.Start());
        }

        public virtual ActionResult UpdateAllPictures()
        {
            Service.UpdateAllPictures();
            return RedirectToSuccessAction(MVC.Common.Start(), "Nuotraukos sukeltos sėkmingai");
        }

        public virtual ActionResult GetMyCategories(string userObjectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            var model = Service.GetMyCategories(userObjectId);
            model.ActionResult = MVC.Account.GetMyCategories();
            var json =
                new { Content = RenderPartialViewToString(MVC.Shared.Views.SimpleListContainer, model) };
            return Json(json);
        }

        private ActionResult OpenAuthRegister(OAuthLoginModel model)
        {
            if (model.ReturnTo.IsNullOrEmpty())
            {
                model.ReturnTo = authentication.DefaultUrl;
            }

            long? fbId = !string.IsNullOrEmpty(model.FacebookId) ? Convert.ToInt64(model.FacebookId) : (long?)null;
            var user = Service.GetOAuthUser(fbId, model.Email);
            if (user == null)
            {
                if (string.IsNullOrEmpty(model.Email))
                {
                    ModelState.Clear();
                    ModelState.AddModelError("", Resource.EmailNotReceived);
                    return Login(model.ReturnTo);
                }

                user = Service.CreateOAuthUser(model);

                if (user != null)
                {
                    authentication.SignIn(model.UserName, model.RememberMe);
                    LogLogin(model.UserName, true);
                    StartTour();
                    RedirectUrl = model.ReturnTo;
                    if (user.DbId.HasValue)
                    {
                        Service.SetFacebookPageLiked(user.DbId.Value, model.IsPageLiked);
                    }

                    return RedirectToAction(MVC.Account.Manifest());
                }
            }
            else
            {
                authentication.SignIn(user.UserName, model.RememberMe);
                LogLogin(user.UserName, true);

                //for early registered users who have facebookId but no email
                if (string.IsNullOrEmpty(CurrentUser.Email) && !string.IsNullOrEmpty(model.FacebookId))
                {
                    Service.SaveFacebookUserEmail(model.Email);
                }
            }

            if (user != null)
            {
                if (user.DbId.HasValue)
                {
                    Service.SetFacebookPageLiked(user.DbId.Value, model.IsPageLiked);
                }

                if (!user.HasSigned)
                {
                    RedirectUrl = model.ReturnTo;
                    return RedirectToAction(MVC.Account.Manifest());
                }
            }

            if (SsoRedirect)
            {
                return RedirectToAction(MVC.Account.RedirectGateway(model.ReturnTo));
            }

            return Redirect(model.ReturnTo);
        }
    }
}
