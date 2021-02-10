using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity.Core;
using System.IO;
using System.Linq;
using System.Web.Mvc;
using Data.Enums;
using Data.ViewModels.Base;
using Data.ViewModels.Comments;
using Data.ViewModels.Voting;
using Framework.Exceptions;
using Framework.Hashing;
using Framework.Mvc;
using Framework.Mvc.Filters;
using Framework.Mvc.Mvc;
using Framework.Strings;
using Globalization.Resources.Services;
using Globalization.Resources.Shared;
using Globalization.Resources.Voting;
using Services.Enums;
using Services.Exceptions;
using Services.ModelServices;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Services.Session;
using Authorize = Web.Infrastructure.Attributes.AuthorizeAttribute;
using ObjectVisibility = Data.Enums.ObjectVisibility;

//Controller for a Voting
namespace Web.Controllers
{
    [ValidateAntiForgeryTokenWrapper(HttpVerbs.Post)]
    public partial class VotingController : SiteBaseServiceController<VotingService>
    {
        private IssueListViews? IssueListView
        {
            get
            {
                var show = Session["IssueListView"];
                if (show == null)
                {
                    return null;
                }

                return (IssueListViews)show;
            }
            set
            {
                Session["IssueListView"] = value;
            }
        }

        private IssueListSorts? IssueListSort
        {
            get
            {
                var show = Session["IssueListSort"];
                if (show == null)
                {
                    return null;
                }

                return (IssueListSorts)show;
            }
            set
            {
                Session["IssueListSort"] = value;
            }
        }

        private IEnumerable<int> SelectedCategories
        {
            get { return (IEnumerable<int>)Session["SelectedCategories"]; }
            set { Session["SelectedCategories"] = value; }
        }

        private List<SimpleListModel> IssuesList
        {
            get
            {
                if (Session["IssuesList"] == null)
                {
                    Session["IssuesList"] = Service.GetIssuesList(GetIssueListView(null), GetIssueListSort(null), SelectedCategories, OrganizationId);
                }

                return (List<SimpleListModel>)Session["IssuesList"];
            }
            set { Session["IssuesList"] = value; }
        }

        public virtual ActionResult GetFinishedDashboard()
        {
            if (!IsAjaxRequest)
            {
                return RedirectToAction(MVC.NewsFeed.Default(CurrentUser.LanguageCode));
            }

            var model = Service.GetTopResults(0);
            return Json(new { Content = RenderPartialViewToString(MVC.NewsFeed.Views.DashboardFinishedIssues, model) });
        }

        public virtual ActionResult GetActiveDashboard()
        {
            if (!IsAjaxRequest)
            {
                return RedirectToAction(MVC.NewsFeed.Default(CurrentUser.LanguageCode));
            }

            var model = Service.GetTopIssues(0);
            return Json(new { Content = RenderPartialViewToString(MVC.NewsFeed.Views.DashboardIssues, model) });
        }

        public VotingController()
        {
        }

        [HttpGet]
        [ImportModelStateFromTempData]
        public virtual ActionResult Index(IssueListViews? issueListView, IssueListSorts? issueListSort, string organizationId)
        {
            var issues = Service.GetIssuePage(0, GetIssueListView(issueListView), GetIssueListSort(issueListSort), SelectedCategories, organizationId);
            issues.ListView = (int)GetIssueListView(issueListView);
            issues.ListSort = (int)GetIssueListSort(issueListSort);
            IssuesList = null;

            if (IsTour)
            {
                var message = Resource.TourIndex;
                if (issues.Items.List.Any())
                {
                    Tour(
                        MVC.Voting.Details(issues.Items.List.First().Id, issues.Items.List.First().Subject.ToSeoUrl()),
                        message);
                }
                else
                {
                    Tour(
                        MVC.Account.Details(CurrentUser.Id, CurrentUser.FullName.ToSeoUrl(), null),
                        message);
                }
            }
            LastListUrl = Request.Url;
            return View(MVC.Voting.Views._Index, issues);
        }

        [HttpGet]
        public virtual ActionResult Results(IssueListViews? issueListView, IssueListSorts? issueListSort, string organizationId)
        {
            var issues = Service.GetResultsPage(0, GetIssueListView(issueListView), GetIssueListSort(issueListSort), SelectedCategories, organizationId);
            IssuesList = Service.GetResultsList(GetIssueListView(issueListView), GetIssueListSort(issueListSort), SelectedCategories, organizationId);
            issues.ListView = (int)GetIssueListView(issueListView);
            issues.ListSort = (int)GetIssueListSort(issueListSort);
            LastListUrl = Request.Url;
            return View(MVC.Voting.Views._Results, issues);
        }

        public virtual ActionResult GetNextPage(int? pageIndex, string organizationId)
        {
            if (Request.HttpMethod == "GET")
            {
                return RedirectToAction(MVC.Voting.Index());
            }

            if (!pageIndex.HasValue)
            {
                return Json(null);
            }

            var issues = Service.GetIssuePage(pageIndex.Value, GetIssueListView(IssueListView), GetIssueListSort(IssueListSort), SelectedCategories, organizationId);

            var json =
                new { Content = RenderPartialViewToString(MVC.Voting.Views.List, issues.Items.List), issues.Items.HasMoreElements, TotalCount = issues.TotalCount };
            return Json(json);
        }

        public virtual ActionResult GetNextResultsPage(int? pageIndex, string organizationId)
        {
            if (!Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Voting.Results());
            }

            if (!pageIndex.HasValue)
            {
                return Json(null);
            }

            var issues = Service.GetResultsPage(pageIndex.Value, GetIssueListView(IssueListView), GetIssueListSort(IssueListSort), SelectedCategories, organizationId);

            var json =
                new { Content = RenderPartialViewToString(MVC.Voting.Views._ResultsList, issues.Items.List), issues.Items.HasMoreElements, issues.TotalCount };
            return Json(json);
        }

        /// <summary>
        /// Creates this instance.
        /// </summary>
        /// <returns></returns>
        [HttpGet, Authorize]
        public virtual ActionResult Create(string organizationId = null, string relatedIdeaId = null, string problemId = null)
        {
            if (!EnsureIsUnique())
            {
                return RedirectToAction(MVC.Account.Details());
            }

            var model = Service.Create(organizationId, relatedIdeaId, problemId);

            return View(MVC.Voting.Views._Create, model);
        }

        /// <summary>
        /// Creates new user.
        /// </summary>
        /// <param name="model">User to create.</param>
        /// <returns>The list view.</returns>
        [HttpPost, Authorize]
        public virtual ActionResult Create(VotingCreateEditModel model)
        {
            if (model.IsPrivateToOrganization && string.IsNullOrEmpty(model.OrganizationId))
            {
                ModelState.AddModelError("IsPrivateToOrganization", Resource.SelectOrganization);
            }

            if (!model.IsPrivateToOrganization && (model.Urls == null || !model.Urls.Any()))
            {
                ModelState.AddModelError("Urls", Resource.UrlIsRequired);
            }

            if (WorkContext.CategoryIds == null && (model.CategoryIds == null || !model.CategoryIds.Any()))
            {
                ModelState.AddModelError("CategoryIds", Globalization.Resources.Idea.Resource.CategoryIdsIsEmpty);
            }

            if (ModelState.IsValid)
            {
                var id = Service.Insert(model);
                if (!string.IsNullOrEmpty(id))
                {
                    TempData["Saved"] = true;
                    return RedirectToSuccessAction(model.IsPrivateToOrganization ? MVC.Organization.Details(model.OrganizationId, model.OrganizationName, OrganizationViews.Issues, null, null, null) : MVC.Voting.Details(id, model.Subject.ToSeoUrl()));
                }
            }

            model = Service.FillCreateEditModel(model);

            return View(MVC.Voting.Views._Create, model);
        }

        /// <summary>
        /// Creates this instance.
        /// </summary>
        /// <returns></returns>
        [HttpGet, ImportModelStateFromTempData, Authorize]
        public virtual ActionResult Edit(string id)
        {
            var model = Service.GetIssueForEdit(id);

            EnsureCanEdit(model);

            return View(MVC.Voting.Views._Create, model);
        }

        [HttpPost, ExportModelStateToTempData, Authorize]
        public virtual ActionResult Edit(VotingCreateEditModel model)
        {
            if (ModelState.IsValid)
            {
                Service.Edit(model);
                return RedirectToAction(MVC.Voting.Details(model.Id, model.Subject.ToSeoUrl()));
            }

            model = Service.FillCreateEditModel(model);
            return View(MVC.Voting.Views._Create, model);
        }

        /// <summary>
        /// Creates this instance.
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [ImportModelStateFromTempData]
        public virtual ActionResult Details(string id, string subject)
        {
            if (string.IsNullOrEmpty(id))
            {
                return RedirectToAction(MVC.Idea.Index());
            }

            try
            {
                var issue = Service.GetIssueView(id);

                if (issue.Visibility == ObjectVisibility.Private &&
                    (CurrentUser.Organizations == null ||
                     !CurrentUser.IsUserInOrganization(issue.OrganizationId)))
                {
                    if (CurrentUser.IsAuthenticated)
                    {
                        return
                            RedirectToFailureAction(
                                MVC.Organization.Details(issue.OrganizationId, issue.OrganizationName.ToSeoUrl(),null, null, null, null),
                                Errors.PageNotPublic);
                    }
                    throw new UnauthorizedAccessException(
                        string.Format(
                            "UnauthorizedAccessException: You don't have access. You're currently logged in as {0}.",
                            CurrentUser.FullName));
                }

                string expectedName = issue.Subject.ToSeoUrl();
                string actualName = (subject ?? "").ToLower();

                // permanently redirect to the correct URL
                if (expectedName != actualName)
                {
                    return RedirectToActionPermanent("Details", "Voting", new {id = issue.Id, subject = expectedName});
                }

                if (issue.Vote.HasValue)
                {
                    issue.VotedString = GetVotedString(issue.Vote.Value);
                }

                if (IsTour)
                {
                    Tour(MVC.About.About(), Resource.TourIssue);
                }

                return View(MVC.Voting.Views._Details, issue);
            }
            catch (ObjectNotFoundException ex)
            {
                return HttpNotFound();
            }
        }


        public virtual ActionResult GetText(string id, string versionId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Voting.Details(id, null));
            }

            var version = Service.GetVersion(id, versionId);
            version.IsForHistory = true;
            return Json(version);
        }

        [ValidateInput(false)]
        public virtual ActionResult AddVersion(string id, string summary)
        {
            if (string.IsNullOrEmpty(summary.Trim()))
            {
                return Json(new { error = Resource.EnterSummary });
            }

            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Voting.Details(id, null));
            }

            var issue = Service.AddNewVersion(id, summary, CurrentUser);
            return Json(new { Text = summary, Versions = RenderPartialViewToString(MVC.Voting.Views._Versions, issue) });
        }

        public virtual ActionResult AddComment(CommentView model, EmbedModel embed)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Voting.Details(model.Id, null));
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var comment = Service.AddNewComment(model.EntryId, model.CommentText, embed, model.ForAgainst);
                    if (comment.VotingStatistics != null && comment.VotingStatistics.Vote.HasValue)
                    {
                        comment.VotingStatistics.VotedString = GetVotedString(comment.VotingStatistics.Vote.Value);
                    }

                    return Json(new
                        {
                            Comment = RenderPartialViewToString(MVC.Comments.Views._Comment, comment),
                            VotingStatistics =
                                    comment.VotingStatistics != null
                                        ? RenderPartialViewToString(MVC.Voting.Views.VotingStatistics,
                                                                    comment.VotingStatistics)
                                        : null,
                            SubscribeMain = RenderPartialViewToString(MVC.Shared.Views.Subscribe, comment.SubscribeMain)
                        });
                }
                catch (Exception ex)
                {
                    return ProcessError(ex);
                }
            }

            return Json(false);
        }

        public virtual ActionResult GetMoreComments(string issueId, ForAgainst? posOrNeg, int? pageIndex)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Voting.Details(issueId, null));
            }

            if (!pageIndex.HasValue)
            {
                return Json(null);
            }
            var comments = Service.GetCommentsMostSupported(issueId, pageIndex.Value, posOrNeg);

            var json =
                new
                {
                    Content = RenderPartialViewToString(MVC.Comments.Views._CommentList, comments.List),
                    comments.HasMoreElements
                };
            return Json(json);
        }

        public virtual ActionResult DeleteComment(string id, string commentId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Voting.Details(id, null));
            }

            var success = Service.DeleteComment(id, commentId);
            return Json(success);
        }

        public virtual ActionResult DeleteCommentComment(string id, string commentId, string commentCommentId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Voting.Details(id, null));
            }

            var success = Service.DeleteCommentComment(id, commentId, commentCommentId);
            return Json(success);
        }

        public virtual ActionResult LikeComment(string id, string commentId, string parentId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Voting.Details(id, null));
            }

            var model = Service.LikeComment(id, commentId, parentId);
            return Json(new { Content = RenderPartialViewToString(MVC.Comments.Views.Like, model) });
        }

        public virtual ActionResult UndoLikeComment(string id, string commentId, string parentId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Voting.Details(id, null));
            }

            var model = Service.UndoLikeComment(id, commentId, parentId);
            return Json(new { Content = RenderPartialViewToString(MVC.Comments.Views.Like, model) });
        }

        public virtual ActionResult AddCommentComment(CommentView model, EmbedModel embed)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Voting.Details(model.EntryId, null));
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var comment = Service.AddNewCommentToComment(model.EntryId, model.Id, model.CommentText, embed);
                    return Json(new
                    {
                        Comment = RenderPartialViewToString(MVC.Comments.Views._CommentComment, comment),
                        Subscribe = RenderPartialViewToString(MVC.Shared.Views.Subscribe, comment.Subscribe),
                        SubscribeMain = RenderPartialViewToString(MVC.Shared.Views.Subscribe, comment.SubscribeMain)
                    });
                }
                catch (Exception ex)
                {
                    return ProcessError(ex);
                }
            }

            return Json(false);
        }

        [Authorize(true)]
        public virtual ActionResult VoteFor(string id)
        {
            return Vote(id, ForAgainst.For);
        }

        [Authorize(true)]
        public virtual ActionResult VoteNeutral(string id)
        {
            return Vote(id, ForAgainst.Neutral);
        }

        [Authorize(true)]
        public virtual ActionResult VoteAgainst(string id)
        {
            return Vote(id, ForAgainst.Against);
        }

        private ActionResult Vote(string id, ForAgainst forAgainst)
        {
            try
            {
                if (IsAjaxRequest)
                {
                    LastVoteReferrerUrl = Request.UrlReferrer;
                }

                var vote = Service.Vote(id, forAgainst);

                if (!IsAjaxRequest)
                {
                    return RedirectToSuccessAction(MVC.NewsFeed.Default(CurrentUser.LanguageCode), Resource.VoteSuccess, true);
                }

                if (!vote.Vote.HasValue)
                {
                    return Json(Resource.VotingFinished);
                }

                vote.VotedString = GetVotedString(forAgainst);

                return
                    Json(
                        new
                        {
                            VotingStatistics = RenderPartialViewToString(MVC.Voting.Views.VotingStatistics, vote),
                            Type = EntryTypes.Issue.ToString(),
                            Progress = RenderPartialViewToString(MVC.Voting.Views.Progress, vote),
                            Subscribe = RenderPartialViewToString(MVC.Shared.Views.Subscribe, vote.Subscribe),
                            ThankYou = RenderPartialViewToString(MVC.Voting.Views.ButtonThankYou)
                        });
            }
            catch (AdditionalUniqueInfoRequiredException ex)
            {
                if (IsAjaxRequest)
                {
                    var result = ProcessError(ex, false) as JsonpResult;
                    result.Data = new
                    {
                        Error = ex.GetType().ToString() + ": " + ex.Message,
                        Content = RenderPartialViewToString(MVC.Account.Views.AdditionalUniqueInfoContents, ex.AdditionalInfo)
                    };
                    return result;
                }

                TempData["OpenAdditinalUniqueInfoDialog"] = ex.AdditionalInfo;
                return Details(id, null);
            }
            catch (Exception e)
            {
                return ProcessError(e, false);
            }
        }

        [Authorize(true)]
        public virtual ActionResult CancelVote(string id)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Voting.Details(id, null));
            }

            var vote = Service.CancelVote(id);

            return Json(new { VotingStatistics = RenderPartialViewToString(MVC.Voting.Views.VotingStatistics, vote), Type = EntryTypes.Issue.ToString() });
        }

        [HttpGet]
        [Authorize]
        public virtual ActionResult EditMyCategories()
        {
            if (IsTour)
            {
                FirstTourPageVisited = true;
                Tour(null, Resource.TourCategories);
            }

            var myCategories = Service.GetMyCategories();
            return View(MVC.Voting.Views._EditMyCategories, myCategories);
        }

        [HttpPost]
        [Authorize]
        public virtual ActionResult EditMyCategories(IList<CategorySelectModel> categories)
        {
            Service.LikeCategories(categories);
            if (IsTour)
            {
                return RedirectToAction(MVC.Account.Details());
            }

            if (Request.QueryString["returnTo"] != null)
            {
                return Redirect(Server.UrlDecode(Request.QueryString["returnTo"]));
            }

            return RedirectToAction(MVC.Account.Details(CurrentUser.Id, null, null));
        }

        public virtual ActionResult AddRelatedIdea(string id, string name)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Voting.Edit(id));
            }

            return Json(new { Content = RenderPartialViewToString(MVC.Voting.Views.RelatedIdea, new RelatedIdeaListItem() { ObjectId = id, Name = name, ChangeState = false }) });
        }

        public virtual ActionResult GetVotedIssues(string userObjectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            var model = Service.GetVotedIssues(userObjectId, 0);
            model.ActionResult = MVC.Voting.GetNextVotedIssuesPage(userObjectId, null);
            var json =
                new { Content = RenderPartialViewToString(MVC.Shared.Views.SimpleListContainer, model) };
            return Json(json);
        }

        public virtual ActionResult GetNextVotedIssuesPage(string userObjectId, int? pageIndex)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            if (!pageIndex.HasValue)
            {
                return Json(null);
            }

            var issues = Service.GetVotedIssues(userObjectId, pageIndex.Value);

            var json =
                new { Content = RenderPartialViewToString(MVC.Shared.Views.SimpleList, issues.List.List), issues.List.HasMoreElements };
            return Json(json);
        }

        public virtual ActionResult GetCommentedIssues(string userObjectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            var model = Service.GetCommentedIssues(userObjectId, 0);
            model.ActionResult = MVC.Voting.GetNextCommentedIssuesPage(userObjectId, null);
            var json =
                new { Content = RenderPartialViewToString(MVC.Shared.Views.SimpleListContainer, model) };
            return Json(json);
        }

        public virtual ActionResult GetNextCommentedIssuesPage(string userObjectId, int? pageIndex)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            if (!pageIndex.HasValue)
            {
                return Json(null);
            }

            var issues = Service.GetCommentedIssues(userObjectId, pageIndex.Value);

            var json =
                new { Content = RenderPartialViewToString(MVC.Shared.Views.SimpleList, issues.List.List), issues.List.HasMoreElements };
            return Json(json);
        }

        public virtual ActionResult GetCreatedIssues(string userObjectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            var model = Service.GetCreatedIssues(userObjectId, 0);
            model.ActionResult = MVC.Voting.GetNextCreatedIssuesPage(userObjectId, null);
            var json =
                new { Content = RenderPartialViewToString(MVC.Shared.Views.SimpleListContainer, model) };
            return Json(json);
        }

        public virtual ActionResult GetNextCreatedIssuesPage(string userObjectId, int? pageIndex)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            if (!pageIndex.HasValue)
            {
                return Json(null);
            }

            var issues = Service.GetCreatedIssues(userObjectId, pageIndex.Value);

            var json =
                new { Content = RenderPartialViewToString(MVC.Shared.Views.SimpleList, issues.List.List), issues.List.HasMoreElements };
            return Json(json);
        }

        [Authorize]
        public virtual ActionResult SaveMyCategories(IList<int> selectedCategoryIds)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Voting.Index());
            }

            Service.LikeCategories(selectedCategoryIds);

            return Json(true);
        }

        public virtual ActionResult FilterPage(List<int> selectedCategoryIds)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Voting.Index());
            }

            SelectedCategories = selectedCategoryIds;
            return GetNextPage(0, null);
        }

        public virtual ActionResult FilterResultsPage(List<int> selectedCategoryIds)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Voting.Results());
            }

            SelectedCategories = selectedCategoryIds;
            return GetNextResultsPage(0, null);
        }

        [Authorize, ValidateInput(false)]
        public virtual ActionResult OfficialVote(string id, ForAgainst forAgainst, string description)
        {
            var model = Service.OfficialVote(id, forAgainst, description);

            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Voting.Details(id, null));
            }

            return Json(true);
        }

        public virtual ActionResult GetAllIdeas()
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Voting.Create());
            }

            return Json(Service.GetIdeas());
        }

        public virtual ActionResult SendNotification(string ideaId)
        {
            return Json(Service.SendNotification(ideaId));
        }

        public virtual ActionResult Delete(string id)
        {
            var result = Service.Delete(id);
            if (result)
            {
                return RedirectToSuccessAction(MVC.Voting.Index(), Resource.DeleteSuccess);
            }

            return RedirectToFailureAction(MVC.Voting.Details(id, null), Resource.DeleteFailed);
        }

        public virtual ActionResult UpdateDb()
        {
            if (!Service.UpdateIssueDb())
            {
                throw new Exception("Failed updating issue DB");
            }

            return RedirectToAction(MVC.Common.Start());
        }

        private static string GetVotedString(ForAgainst pn)
        {
            if (pn == ForAgainst.For)
            {
                return Resource.VotedFor;
            }

            if (pn == ForAgainst.Against)
            {
                return Resource.VotedAgainst;
            }

            return string.Empty;
        }

        private IssueListViews GetIssueListView(IssueListViews? view)
        {
            if (view.HasValue)
            {
                IssueListView = view.Value;
                return view.Value;
            }

            if (IssueListView.HasValue)
            {
                return IssueListView.Value;
            }

            return IssueListViews.Interesting;
        }

        private IssueListSorts GetIssueListSort(IssueListSorts? sort)
        {
            if (sort.HasValue)
            {
                IssueListSort = sort.Value;
                return sort.Value;
            }

            if (IssueListSort.HasValue)
            {
                return IssueListSort.Value;
            }

            if (CurrentUser.IsAuthenticated)
            {
                return IssueListSorts.Nearest;
            }

            return IssueListSorts.MostActive;
        }

        private void EnsureCanEdit(VotingCreateEditModel model)
        {
            if (!model.CanCurrentUserEdit)
            {
                var currentUserName = CurrentUser.FullName;

                throw new UnauthorizedAccessException(string.Format("UnauthorizedAccessException: You don't have access. You're currently logged in as {0}.",
                                                      currentUserName));
            }
        }

        public virtual ActionResult GetMoreTopFinishedIssues(int? pageIndex)
        {
            var model = Service.GetTopResults(pageIndex ?? 0);
            return Json(new { Content = RenderPartialViewToString(MVC.NewsFeed.Views.FinishedIssuesList, model), model.Items.HasMoreElements });
        }

        public virtual ActionResult GetMoreTopIssues(int? pageIndex)
        {
            var model = Service.GetTopIssues(pageIndex ?? 0);
            return Json(new { Content = RenderPartialViewToString(MVC.NewsFeed.Views.IssuesList, model), model.Items.HasMoreElements });
        }

        public virtual ActionResult PreviousIssue(string id)
        {
            if (IssuesList == null || IssuesList.Count == 0)
            {
                return RedirectToAction(MVC.Voting.Details(id, null));
            }

            var current = IssuesList.SingleOrDefault(i => i.Id == id);
            var index = IssuesList.IndexOf(current) - 1;
            if (index < 0)
            {
                index = IssuesList.Count - 1;
            }

            return RedirectToAction(MVC.Voting.Details(IssuesList[index].Id, IssuesList[index].Subject.ToSeoUrl()));
        }

        public virtual ActionResult NextIssue(string id)
        {
            if (IssuesList == null || IssuesList.Count == 0)
            {
                return RedirectToAction(MVC.Voting.Details(id, null));
            }

            var current = IssuesList.SingleOrDefault(i => i.Id == id);
            var index = IssuesList.IndexOf(current) + 1;
            if (index >= IssuesList.Count)
            {
                index = 0;
            }

            if (IssuesList.Any())
            {
                return RedirectToAction(MVC.Voting.Details(IssuesList[index].Id, IssuesList[index].Subject.ToSeoUrl()));
            }

            return RedirectToAction(MVC.Voting.Details(id, null));
        }

        [Authorize]
        public virtual ActionResult GetMatchedIssues(string prefix)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Idea.Create());
            }

            var model = Service.GetMatchedIssues(prefix);
            return Json(model);
        }

        [Authorize, IgnoreStripWhitespaceAttribute]
        public virtual ActionResult GenerateReport(string id)
        {
            if (IsJsonRequest)
            {
                return Jsonp(Service.GetIssueDocumentModel(id));
            }

            var pdf = Service.GeneratePdf(id);

            var signedPdf = PdfSign.SignPdfFile(pdf, null, SharedStrings.SiteName);
            return File(signedPdf, "Application/pdf");
        }

        public virtual ActionResult CompareHistoryVersions(string id, string historyId1, string historyId2)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(historyId1) ||
                string.IsNullOrEmpty(historyId2) || historyId1 == historyId2)
            {
                return Json(false);
            }

            var html = Service.CompareVersions(id, historyId1, historyId2);
            return Json(new { Content = html });
        }
    }
}
