using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Data.Enums;
using Data.ViewModels.Account;
using Data.ViewModels.Base;
using Framework.Infrastructure;
using Framework.Mvc.Filters;
using Globalization.Resources.NewsFeed;
using Services.ModelServices;
using Authorize = Web.Infrastructure.Attributes.AuthorizeAttribute;

namespace Web.Controllers
{
    public partial class CommonController : SiteBaseController
    {
        public virtual ActionResult Start()
        {
            if(IsAuthenticated)
            {
                return RedirectToAction(MVC.NewsFeed.Index());
            }

            //return RedirectToAction(MVC.Account.Login());
            return RedirectToAction(MVC.NewsFeed.Default(CurrentUser.LanguageCode));
        }

        public virtual ActionResult Redirect()
        {
            return View(MVC.Error.Views.Redirect);
        }

        public virtual ActionResult ValidateShortLink(string shortLink)
        {
            var service = ServiceLocator.Resolve<ShortLinkService>();
            return Json(!service.ShortLinkExists(shortLink), JsonRequestBehavior.AllowGet);
        }

        public virtual ActionResult ResolveShortLink(string id)
        {
            var service = ServiceLocator.Resolve<ShortLinkService>();
            return RedirectPermanent(service.GetFullLink(id));
        }

        public virtual ActionResult UpdateUserReputations()
        {
            var service = ServiceLocator.Resolve<ActionService>();
            service.UpdateUserReputations();
            return RedirectToSuccessAction(MVC.NewsFeed.Default(CurrentUser.LanguageCode), Resource.ReputationsUpdated);
        }

        public virtual ActionResult UpdateShortLinks()
        {
            var service = (VotingService)ServiceLocator.Resolve(typeof(VotingService));
            service.UpdateShortLinks();

            var ideaService = (IdeaService)ServiceLocator.Resolve(typeof(IdeaService));
            ideaService.UpdateShortLinks();

            var userService = (UserService)ServiceLocator.Resolve(typeof(UserService));
            userService.UpdateShortLinks();

            var orgService = ServiceLocator.Resolve<OrganizationService>();
            orgService.UpdateShortLinks();

            return Start();
        }

        public virtual ActionResult GetCommentSupporters(string entryId, string commentId, EntryTypes? type, string parentId)
        {
            if(!Request.IsAjaxRequest())
            {
                return Start();
            }

            if (string.IsNullOrEmpty(entryId) || string.IsNullOrEmpty(commentId) || !type.HasValue)
            {
                return Start();
            }

            SimpleListContainerModel model = null;
            if(type == EntryTypes.Issue)
            {
                var service = (VotingService)ServiceLocator.Resolve(typeof (VotingService));
                model = service.GetCommentSupporters(0, entryId, commentId, parentId);
            }
            else if (type == EntryTypes.Idea)
            {
                var service = (IdeaService)ServiceLocator.Resolve(typeof(IdeaService));
                model = service.GetCommentSupporters(0, entryId, commentId, parentId);
            }
            else if (type == EntryTypes.Problem)
            {
                var service = (ProblemService)ServiceLocator.Resolve(typeof(ProblemService));
                model = service.GetCommentSupporters(0, entryId, commentId, null);
            }
            else if (type == EntryTypes.User)
            {
                var service = (UserService)ServiceLocator.Resolve(typeof(UserService));
                model = service.GetCommentSupporters(0, entryId, commentId, parentId);
            }
            else
            {
                return Json(null);
            }

            model.ActionResult = MVC.Common.GetNextGetCommentSupportersPage(null, entryId, commentId, type.Value, parentId);
            var json =
                new { Content = RenderPartialViewToString(MVC.Shared.Views.SimpleListContainer, model) };
            return Json(json);
        }

        [AjaxOnly]
        public virtual ActionResult GetNextGetCommentSupportersPage(int? pageIndex, string entryId, string commentId, EntryTypes type, string parentId)
        {
            if (!pageIndex.HasValue)
            {
                return Json(null);
            }

            SimpleListContainerModel model = null;
            if (type == EntryTypes.Issue)
            {
                var service = (VotingService)ServiceLocator.Resolve(typeof(VotingService));
                model = service.GetCommentSupporters(pageIndex.Value, entryId, commentId, parentId);
            }
            else if (type == EntryTypes.Idea)
            {
                var service = (IdeaService)ServiceLocator.Resolve(typeof(IdeaService));
                model = service.GetCommentSupporters(pageIndex.Value, entryId, commentId, parentId);
            }
            else if (type == EntryTypes.Problem)
            {
                var service = (ProblemService)ServiceLocator.Resolve(typeof(ProblemService));
                model = service.GetCommentSupporters(pageIndex.Value, entryId, commentId, null);
            }
            else
            {
                return Json(null);
            }

            var json =
                new { Content = RenderPartialViewToString(MVC.Shared.Views.SimpleList, model.List.List), model.List.HasMoreElements };
            return Json(json);
        }

        [Framework.Mvc.Filters.Authorize]
        public virtual ActionResult AddWebSite(int index, string listName = "Urls")
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                if (Request.UrlReferrer != null)
                {
                    return Redirect(Request.UrlReferrer.ToString());
                }
            }

            var model = new UrlEditModel(listName);

            return Json(new { Content = RenderPartialViewToString(MVC.Shared.Views.WebSite, model) });
        }

        [Authorize]
        public virtual ActionResult Unsubscribe(string id, EntryTypes? type = null)
        {
            var service = (ActionService)ServiceLocator.Resolve(typeof(ActionService));
            var result = service.Subscribe(id, CurrentUser.DbId.Value, type, false);
            return Json(new { Content = RenderPartialViewToString(MVC.Shared.Views.Subscribe, result) });
        }

        [Authorize]
        public virtual ActionResult Subscribe(string id, EntryTypes? type = null)
        {
            var service = (ActionService)ServiceLocator.Resolve(typeof(ActionService));
            var result = service.Subscribe(id, CurrentUser.DbId.Value, type, true);
            return Json(new { Content = RenderPartialViewToString(MVC.Shared.Views.Subscribe, result) });
        }
    }
}