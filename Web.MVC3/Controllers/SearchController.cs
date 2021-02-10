using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Data.ViewModels.Search;
using Framework.Lists;
using PagedList;
using Services.ModelServices;
using Authorize = Web.Infrastructure.Attributes.AuthorizeAttribute;

namespace Web.Controllers
{
    public partial class SearchController : SiteBaseServiceController<SearchService>
    {
        SearchModel Results
        {
            get { return Session["SearchResults"] as SearchModel; }
            set { Session["SearchResults"] = value; }
        }

        public virtual ActionResult Search(string searchText)
        {
            Results = Service.Search(searchText);
            Results.ExpandableList = Results.List.ToPagedList(1, 20);
            return View(Results);
        }

        public virtual ActionResult GetNextPage(int? pageIndex)
        {
            if (!pageIndex.HasValue)
            {
                return Json(null);
            }

            Results.ExpandableList = Results.List.ToPagedList(pageIndex.Value + 1, 20);

            var json =
                new { Content = RenderPartialViewToString(MVC.Search.Views.List, Results.ExpandableList), HasMoreElements = Results.ExpandableList.HasNextPage };
            return Json(json);
        }

        public virtual ActionResult BuildIndex()
        {
            Service.BuildIndex();
            return new EmptyResult();
        }
    }
}
