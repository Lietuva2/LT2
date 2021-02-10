using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity.Core;
using System.Linq;
using System.Web.Mvc;
using Data.Enums;
using Data.ViewModels.Base;
using Data.ViewModels.Comments;
using Data.ViewModels.Problem;
using Framework.Mvc.Filters;
using Framework.Strings;
using Services.Enums;
using Services.ModelServices;
using Authorize = Web.Infrastructure.Attributes.AuthorizeAttribute;

//Controller for a Problems
namespace Web.Controllers
{
    [ValidateAntiForgeryTokenWrapper(HttpVerbs.Post)]
    public partial class ProblemController : SiteBaseServiceController<ProblemService>
    {
        private ProblemListViews? ProblemListView
        {
            get
            {
                var show = Session["ProblemListView"];
                if (show == null)
                {
                    return null;
                }

                return (ProblemListViews)show;
            }
            set
            {
                Session["ProblemListView"] = value;
            }
        }

        private ProblemListSorts? ProblemListSort
        {
            get
            {
                var show = Session["ProblemListSort"];
                if (show == null)
                {
                    return null;
                }

                return (ProblemListSorts)show;
            }
            set
            {
                Session["ProblemListSort"] = value;
            }
        }

        private IEnumerable<int> SelectedCategories
        {
            get { return (IEnumerable<int>)Session["SelectedCategories"]; }
            set { Session["SelectedCategories"] = value; }
        }

        public ProblemController()
        {
        }

        [HttpGet]
        public virtual ActionResult Index(ProblemListViews? problemListView, ProblemListSorts? problemListSort, string organizationId = null, string problemId = null)
        {
            try
            {
                var model = Service.GetProblemPage(0, GetProblemListView(problemListView),
                                                   GetProblemListSort(problemListSort), SelectedCategories,
                                                   organizationId, problemId);
                model.ListView = (int) GetProblemListView(problemListView);
                model.ListSort = (int) GetProblemListSort(problemListSort);

                return View(MVC.Problem.Views.Index, model);
            }
            catch (ObjectNotFoundException ex)
            {
                return HttpNotFound();
            }
        }

        public virtual ActionResult Single(string problemId)
        {
            if (!IsAjaxRequest || Request.HttpMethod == "GET")
            {
                return Index(null, null, null, problemId);
            }

            var model = Service.GetProblemIndexItem(problemId, null, renderCollapsed: false, getChildObjects: true);

            return Json(new {Content = RenderPartialViewToString(MVC.Problem.Views.ListItem, model), Id = model.Id});
        }
        
        public virtual ActionResult GetNextPage(int? pageIndex, string organizationId)
        {
            if (Request.HttpMethod == "GET")
            {
                return RedirectToAction(MVC.Problem.Index());
            }

            if(!pageIndex.HasValue)
            {
                return Json(null);
            }

            var model = Service.GetProblemPage(pageIndex.Value, GetProblemListView(ProblemListView), GetProblemListSort(ProblemListSort), SelectedCategories, organizationId, null);

            var json =
                new { Content = RenderPartialViewToString(MVC.Problem.Views.List, model.Items.List), model.Items.HasMoreElements, TotalCount = model.TotalCount };
            return Json(json);
        }

        /// <summary>
        /// Creates new user.
        /// </summary>
        /// <param name="model">User to create.</param>
        /// <returns>The list view.</returns>
        [HttpPost, Authorize]
        public virtual ActionResult Create(ProblemCreateEditModel model, EmbedModel embed)
        {
            if (!EnsureIsUnique())
            {
                return RedirectToAction(MVC.Account.Details());
            }

            if (ModelState.IsValid)
            {
                var result = Service.Insert(model, embed);
                return Json(new {Content = RenderPartialViewToString(MVC.Problem.Views.List, new List<ProblemIndexItemModel>{result}), Id = result.Id});
            }

            return Json(false);
        }

        [Authorize]
        public virtual ActionResult AddComment(CommentView model, EmbedModel embed)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Problem.Index());
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var comment = Service.AddNewComment(model.EntryId, model.CommentText, embed);
                    return Json(new {Comment = RenderPartialViewToString(MVC.Comments.Views._Comment, comment),
                                     SubscribeMain = RenderPartialViewToString(MVC.Shared.Views.Subscribe, comment.SubscribeMain)
                    });
                }
                catch (Exception ex)
                {
                    return ProcessError(ex);
                }
            }

            return Json(new
            {
                error = (from v in ModelState.Values
                         from e in v.Errors
                         select e.ErrorMessage).Concatenate(";")
            });
        }

        public virtual ActionResult GetMoreComments(string problemId, int? pageIndex)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Problem.Index());
            }

            if (!pageIndex.HasValue)
            {
                return Json(null);
            }
            var comments = Service.GetCommentsMostRecent(problemId, pageIndex.Value, null, true);

            var json =
                new { Content = RenderPartialViewToString(MVC.Comments.Views._CommentList, comments.List),
                       comments.HasMoreElements};
            return Json(json);
        }

        [Authorize]
        public virtual ActionResult DeleteComment(string id, string commentId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Problem.Index());
            }

            var success = Service.DeleteComment(id, commentId);
            return Json(success);
        }

        [Authorize]
        public virtual ActionResult LikeComment(string id, string commentId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Problem.Index());
            }

            return Json(new { Content = RenderPartialViewToString(MVC.Comments.Views.Like, Service.LikeComment(id, commentId, null)) });
        }

        [Authorize]
        public virtual ActionResult UndoLikeComment(string id, string commentId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Problem.Index());
            }

            return Json(new { Content = RenderPartialViewToString(MVC.Comments.Views.Like, Service.UndoLikeComment(id, commentId, null)) });
        }

        [Authorize]
        public virtual ActionResult VoteFor(string id)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Problem.Index());
            }

            if (!EnsureIsUnique())
            {
                return RedirectToAction(MVC.Account.Details());
            }

            var model = Service.Vote(id, ForAgainst.For);
            
            return Json(new {Content = RenderPartialViewToString(MVC.Problem.Views.Voting, model), Subscribe = RenderPartialViewToString(MVC.Shared.Views.Subscribe, model.Subscribe)});
        }

        [Authorize]
        public virtual ActionResult VoteAgainst(string id)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Problem.Index());
            }

            if (!EnsureIsUnique())
            {
                return RedirectToAction(MVC.Account.Details());
            }

            var model = Service.Vote(id, ForAgainst.Against);

            return Json(new { Content = RenderPartialViewToString(MVC.Problem.Views.Voting, model), Subscribe = RenderPartialViewToString(MVC.Shared.Views.Subscribe, model.Subscribe)});
        }

        public virtual ActionResult GetCreatedProblems(string userObjectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            var model = Service.GetCreatedProblems(userObjectId, 0);
            model.ActionResult = MVC.Problem.GetNextCreatedProblemsPage(userObjectId, null);
            var json =
                new { Content = RenderPartialViewToString(MVC.Shared.Views.SimpleListContainer, model) };
            return Json(json);
        }

        public virtual ActionResult GetNextCreatedProblemsPage(string userObjectId, int? pageIndex)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            if (!pageIndex.HasValue)
            {
                return Json(null);
            }

            var model = Service.GetCreatedProblems(userObjectId, pageIndex.Value);

            var json =
                new { Content = RenderPartialViewToString(MVC.Shared.Views.SimpleList, model.List.List), model.List.HasMoreElements };
            return Json(json);
        }

        public virtual ActionResult GetProblemSupporters(string problemId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Problem.Index());
            }

            var model = Service.GetProblemSupporters(problemId, 0);
            model.ActionResult = MVC.Problem.GetNextProblemSupportersPage(problemId, null);
            var json =
                new { Content = RenderPartialViewToString(MVC.Shared.Views.SimpleListContainer, model) };
            return Json(json);
        }

        public virtual ActionResult GetNextProblemSupportersPage(string problemId, int? pageIndex)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Problem.Index());
            }

            if (!pageIndex.HasValue)
            {
                return Json(null);
            }

            var model = Service.GetProblemSupporters(problemId, pageIndex.Value);

            var json =
                new { Content = RenderPartialViewToString(MVC.Shared.Views.SimpleList, model.List.List), model.List.HasMoreElements };
            return Json(json);
        }

        [Authorize]
        public virtual ActionResult SaveMyCategories(IList<int> selectedCategoryIds)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Problem.Index());
            }

            Service.LikeCategories(selectedCategoryIds);

            return Json(true);
        }

        public virtual ActionResult FilterPage(List<int> selectedCategoryIds)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Problem.Index());
            }

            SelectedCategories = selectedCategoryIds;
            return GetNextPage(0, null);
        }

        public virtual ActionResult GetMatchedProblems(string prefix)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Problem.Index());
            }

            var model = Service.GetMatchedProblems(prefix);
            return Json(model);
        }

        [Authorize]
        public virtual ActionResult AddRelatedProblem(string id, string text)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Problem.Index());
            }

            var model = Service.GetProblemIndexItem(id, text);
            return Json(new {Content = RenderPartialViewToString(MVC.Problem.Views.Problem, model)});
        }

        [Authorize]
        public virtual ActionResult AddRelatedIdea(string problemId, string ideaId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Problem.Index());
            }

            var result = Service.AddRelatedIdea(problemId, ideaId);
            return Json(new {Content = RenderPartialViewToString(MVC.Problem.Views.Idea, result)});
        }

        [Authorize]
        public virtual ActionResult Delete(string problemId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Problem.Index());
            }

            return Json(Service.Delete(problemId));
        }

        private ProblemListViews GetProblemListView(ProblemListViews? view)
        {
            if (view.HasValue)
            {
                ProblemListView = view.Value;
                return view.Value;
            }

            if (ProblemListView.HasValue)
            {
                return ProblemListView.Value;
            }

            return ProblemListViews.Interesting;
        }

        private ProblemListSorts GetProblemListSort(ProblemListSorts? sort)
        {
            if (sort.HasValue)
            {
                ProblemListSort = sort.Value;
                return sort.Value;
            }

            if (ProblemListSort.HasValue)
            {
                return ProblemListSort.Value;
            }

            if (CurrentUser.IsAuthenticated)
            {
                return ProblemListSorts.Newest;
            }

            return ProblemListSorts.MostSupported;
        }

        public virtual ActionResult DeleteRelatedIdea(string ideaId, string problemId)
        {
            return Json(Service.DeleteRelatedIdea(ideaId, problemId));
        }

        public virtual ActionResult UpdateDb()
        {
            if (!Service.UpdateProblemDb())
            {
                throw new Exception("Failed updating idea DB");
            }

            return RedirectToAction(MVC.Common.Start());
        }
    }
}
