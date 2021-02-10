using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceModel.Syndication;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using Data.Enums;
using Framework.Mvc.Filters;
using Framework.Mvc.Mvc;
using Framework.Mvc.Strings;
using Framework.Strings;
using Services.Enums;
using Services.ModelServices;
using Services.Session;
using Authorize = Web.Infrastructure.Attributes.AuthorizeAttribute;

//Controller for a Voting
namespace Web.Controllers
{
    [ValidateAntiForgeryTokenWrapper(HttpVerbs.Post)]
    public partial class NewsFeedController : SiteBaseServiceController<NewsFeedService>
    {

        public string[] Backgrounds
        {
            get
            {
                if (HttpContext.Application["Backgrounds"] == null)
                {
                    var files = new List<string>();
                    var folder = new DirectoryInfo(Server.MapPath(Links.Content.Images.Photos.Url()));
                    foreach (var file in folder.GetFiles())
                    {
                        var path = @"~\" + file.FullName.Replace(HttpContext.Request.PhysicalApplicationPath,
                            String.Empty);
                        path = VirtualPathUtility.ToAbsolute(path);
                        files.Add(path);
                    }

                    HttpContext.Application["Backgrounds"] = files.ToArray();
                }

                return (string[]) HttpContext.Application["Backgrounds"];
            }
        }

        public int? UnreadNewsCount
        {
            get
            {
                var count = Session["UnreadNewsCount"];
                if (count == null)
                {
                    return null;
                }

                if ((int) count == 0)
                {
                    return null;
                }

                return (int)count;
            }
            set
            {
                if (value == 0)
                {
                    Session["UnreadNewsCount"] = null;
                }
                else
                {
                    Session["UnreadNewsCount"] = value;
                }
            }
        }

        public NewsFeedController()
        {
        }

        [HttpGet]
        public virtual ActionResult Index(NewsFeedListViews listView = NewsFeedListViews.MyNews)
        {
            if (WorkContext.CategoryIds != null && listView != NewsFeedListViews.AllNews)
            {
                return RedirectToRoute("AllNewsFeed");   
            }

            if (!CurrentUser.IsAuthenticated && listView == NewsFeedListViews.MyNews )
            {
                return RedirectToRoute("AllNewsFeed");
            }

            var issues = Service.GetNewsFeedPage(0, listView);
            ViewData["NewsFeedListView"] = listView;
            LastListUrl = Request.Url;
            if (listView == NewsFeedListViews.MyNews)
            {
                UnreadNewsCount = null;
            }

            return View(MVC.NewsFeed.Views._Index, issues);
        }

        //[HttpGet]
        //public virtual async Task<ActionResult> IndexAsync(NewsFeedListViews? listView)
        //{
        //    ViewData["NewsFeedListView"] = GetListView(listView);
        //    LastListUrl = Request.Url;

        //    var issues = await Service.GetNewsFeedPageAsync(0, GetListView(listView));
        //    UnreadNewsCount = null;

        //    return View(MVC.NewsFeed.Views._Index, issues);
        //}

        public virtual ActionResult GetNextPage(int? pageIndex, NewsFeedListViews view = NewsFeedListViews.MyNews)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.NewsFeed.Index());
            }

            if (!pageIndex.HasValue)
            {
                return Json(null);
            }
            var issues = Service.GetNewsFeedPage(pageIndex.Value, view);

            var json =
                new
                    {
                        Content = RenderPartialViewToString(MVC.NewsFeed.Views.List, issues.List.List),
                        issues.List.HasMoreElements
                    };
            return Json(json);
        }

        public virtual ActionResult GetUnreadNews(DateTime lastQueryDate, NewsFeedListViews view = NewsFeedListViews.MyNews)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.NewsFeed.Index());
            }

            if (lastQueryDate.AddHours(12) < DateTime.Now)
            {
                lastQueryDate = DateTime.Now.AddHours(-12);
            }

            var issues = Service.GetNewsFeedPage(0, view, lastQueryDate);
            if (view == NewsFeedListViews.MyNews)
            {
                UnreadNewsCount = null;
            }

            var json =
                new { Content = RenderPartialViewToString(MVC.NewsFeed.Views.List, issues.List.List), Date = DateTime.Now.ToString() };
            return Json(json);
        }

        public virtual ActionResult GetUnreadNewsCount()
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.NewsFeed.Index());
            }

            UnreadNewsCount = Service.GetUnreadNewsCount();

            return Json(new { Count = UnreadNewsCount });
        }

        public virtual ActionResult GetUserActivity(string userObjectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, UserViews.Activity));
            }

            var model = Service.GetUserActivityPage(userObjectId, 0);
            return Json(new { Content = RenderPartialViewToString(MVC.NewsFeed.Views.UserActivityListContainer, model) });
        }

        public virtual ActionResult GetNextUserActivityPage(string userObjectId, int? pageIndex)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, UserViews.Activity));
            }

            if (!pageIndex.HasValue)
            {
                return Json(null);
            }

            var model = Service.GetUserActivityPage(userObjectId, pageIndex.Value);

            var json =
                new { Content = RenderPartialViewToString(MVC.NewsFeed.Views.List, model.List), model.HasMoreElements };
            return Json(json);
        }

        public virtual ActionResult GetUserReputation(string userObjectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, UserViews.Reputation));
            }

            var model = Service.GetUserReputationPage(userObjectId, 0);
            return Json(new { Content = RenderPartialViewToString(MVC.NewsFeed.Views.UserReputationListContainer, model) });
        }

        public virtual ActionResult GetNextUserReputationPage(string userObjectId, int? pageIndex)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, UserViews.Reputation));
            }

            if (!pageIndex.HasValue)
            {
                return Json(null);
            }

            var model = Service.GetUserReputationPage(userObjectId, pageIndex.Value);

            var json =
                new { Content = RenderPartialViewToString(MVC.NewsFeed.Views.List, model.List), model.HasMoreElements };
            return Json(json);
        }

        public virtual ActionResult GetNextOrganizationActivityPage(string organizationId, int? pageIndex)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Organization.Details(organizationId, null, null, null, null, null));
            }

            if (!pageIndex.HasValue)
            {
                return Json(null);
            }

            var model = Service.GetOrganizationActivityPage(organizationId, pageIndex.Value);

            var json =
                new { Content = RenderPartialViewToString(MVC.NewsFeed.Views.List, model.List), model.HasMoreElements };
            return Json(json);
        }

        public virtual ActionResult Delete(int userDbId, string objectId, int actionTypeId, string relatedObjectId, string text, bool isPrivate, string organizationId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.NewsFeed.Index());
            }

            return Json(Service.Delete(userDbId, objectId, actionTypeId, relatedObjectId, text, isPrivate, organizationId));
        }

        public virtual ActionResult GetNewsFeedGroupUsers(NewsFeedListViews? view, int? actionTypeId, string objectId, DateTime? date, string relatedObjectId, string organizationId, string text, bool? isPrivate)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest() || !actionTypeId.HasValue || !view.HasValue || string.IsNullOrEmpty(objectId) || !date.HasValue || !isPrivate.HasValue)
            {
                return RedirectToAction(MVC.NewsFeed.Index(view ?? NewsFeedListViews.MyNews));
            }

            var model = Service.GetNewsFeedGroupUsers(0, view.Value, actionTypeId.Value, objectId, date.Value, relatedObjectId, organizationId, text, isPrivate.Value);
            model.ActionResult = MVC.NewsFeed.GetNextNewsFeedGroupUsersPage(null, view, actionTypeId, objectId, date, relatedObjectId, organizationId, text, isPrivate);
            var json =
                new { Content = RenderPartialViewToString(MVC.Shared.Views.SimpleListContainer, model) };
            return Json(json);
        }

        public virtual ActionResult GetNextNewsFeedGroupUsersPage(int? pageIndex, NewsFeedListViews? view, int? actionTypeId, string objectId, DateTime? date, string relatedObjectId, string organizationId, string text, bool? isPrivate)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest() || !actionTypeId.HasValue || !view.HasValue || string.IsNullOrEmpty(objectId) || !date.HasValue || !isPrivate.HasValue)
            {
                return RedirectToAction(MVC.NewsFeed.Index(view ?? NewsFeedListViews.MyNews));
            }

            if (!pageIndex.HasValue)
            {
                return Json(null);
            }

            var model = Service.GetNewsFeedGroupUsers(pageIndex.Value, view.Value, actionTypeId.Value, objectId, date.Value, relatedObjectId, organizationId, text, isPrivate.Value);

            var json =
                new { Content = RenderPartialViewToString(MVC.Shared.Views.SimpleList, model.List.List), model.List.HasMoreElements };
            return Json(json);
        }

        [HttpGet]
        [ImportModelStateFromTempData]
        public virtual ActionResult Default(string lang = null)
        {
            if (!string.IsNullOrEmpty(lang))
            {
                var langModel = Service.SetLanguage(lang);
                this.SetLanguage(langModel.Code, langModel.Name);
            }
            else
            {
                return RedirectToAction(MVC.NewsFeed.Default(CurrentUser.LanguageCode));
            }

            var model = Service.GetStartPage();
            model.ImageUrl = GetRandomImageUrl();
            LastListUrl = Request.Url;
            return View("Default", model);
        }

        private string GetRandomImageUrl()
        {
            return Backgrounds[new Random().Next(Backgrounds.Length)];
        }

        [HttpGet]
        public virtual async Task<ActionResult> DefaultAsync()
        {
            var model = await Service.GetStartPageAsync();
            LastListUrl = Request.Url;
            return View(MVC.NewsFeed.Views.Default, model);
        }

        public virtual ActionResult Rss(int userId, string userName)
        {
            if(!Service.CheckUser(userId, userName))
            {
                return new EmptyResult();
            }

            var model = Service.GetNewsFeedPage(userId);
            SyndicationFeed feed =
        new SyndicationFeed("Lietuva 2.0",
                            "Lietuva 2.0 naujienos",
                            new Uri("https://www.lietuva2.lt/naujienos"),
                            userId.ToString(),
                            DateTime.Now);
            List<SyndicationItem> items = new List<SyndicationItem>();
            
            foreach(var item in model.List.List)
            {
                items.Add(new SyndicationItem(item.Subject.GetPlainText(),
                                    new TextSyndicationContent(Web.Helpers.SpecificHtmlHelpers.GetNewsFeedEntry(item) + "<br/>" + (item.Problem != null ? item.Subject : "") + "<br/>" + item.Text.NewLineToHtml(), TextSyndicationContentKind.Html),
                                    new Uri(item.Link ?? "www.lietuva2.lt", UriKind.RelativeOrAbsolute),
                                    item.ObjectId+item.Date.ToShortDateString()+item.Date.ToShortTimeString(),
                                    item.Date));
            }
            
            feed.Items = items;

            return new RssResult(feed);
        }
    }
}
