using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using Data.ViewModels.Base;
using Framework.Strings;
using Globalization.Resources.Google;

using Google;
using Google.Apis.Auth.OAuth2.Mvc;
using Google.Apis.Drive.v2;
using Google.Apis.Services;

using Services.Enums;
using Services.Infrastructure;

using Web.Infrastructure.Google;

namespace Web.Controllers
{
    public partial class AuthCallbackController : Google.Apis.Auth.OAuth2.Mvc.Controllers.AuthCallbackController
    {
        protected override Google.Apis.Auth.OAuth2.Mvc.FlowMetadata FlowData
        {
            get { return new AppFlowMetadata(this); }
        }
    }

    public partial class GoogleController : SiteBaseController
    {
        protected DriveService GoogleService
        {
            get
            {
                return (DriveService)Session["Google.DriveService"];
            }
            set
            {
                Session["Google.DriveService"] = value;
            }
        }

        public async virtual Task<ActionResult> GoogleAuth(string returnUrl, CancellationToken cancellationToken)
        {
            var result = await new AuthorizationCodeMvcApp(this, new AppFlowMetadata(this)).
                AuthorizeAsync(cancellationToken);

            if (result.Credential != null)
            {
                this.GoogleService = new DriveService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = result.Credential,
                    ApplicationName = "Lietuva 2.0"
                });

                return Redirect(returnUrl);
            }

            if (IsAjaxRequest)
            {
                return Json(new { RedirectUrl = result.RedirectUri });
            }
            else
            {
                return Redirect(result.RedirectUri);
            }
        }

        public async virtual Task<ActionResult> CreateDoc(string subject, CancellationToken cancellationToken)
        {
            if (!Request.IsAjaxRequest())
            {
                return RedirectToFailureAction(MVC.NewsFeed.Default(CurrentUser.LanguageCode), Resource.Error);
            }

            if (!IsGoogleAuthenticated)
            {
                return Json(false);
            }

            try
            {
                if (string.IsNullOrEmpty(subject))
                {
                    subject = Resource.NoName;
                }
                var file = GoogleAuthUtils.Create(GoogleService, subject.LimitLength(100));
                return Json(new { Content = RenderPartialViewToString(MVC.Shared.Views.Attachment, new UrlViewModel() { Url = file.AlternateLink, Title = file.Title, IconUrl = CustomAppSettings.GoogleDocIconUrl }) });
            }
            catch (GoogleApiException e)
            {
                if (e.HttpStatusCode == HttpStatusCode.Unauthorized)
                {
                    return Json(new { error = "Unauthorized" });
                }

                return Json(new { error = e.Message });
            }
        }

        public virtual ActionResult SelectDocs(List<UrlViewModel> docs)
        {
            if (!Request.IsAjaxRequest())
            {
                return RedirectToFailureAction(MVC.NewsFeed.Default(CurrentUser.LanguageCode), Resource.Error);
            }

            if (!IsGoogleAuthenticated)
            {
                return Json(false);
            }

            foreach (var doc in docs)
            {
                GoogleAuthUtils.UpdatePermissions(GoogleService, doc.Id);
            }

            string result = string.Empty;
            foreach (var doc in docs)
            {
                result += RenderPartialViewToString(MVC.Shared.Views.Attachment, doc);
            }

            return Json(new { Content = result });
        }
    }
}
