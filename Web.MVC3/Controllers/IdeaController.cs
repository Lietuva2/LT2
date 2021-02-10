using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity.Core;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Mvc;
using Data.Enums;
using Data.ViewModels.Base;
using Data.ViewModels.Comments;
using Data.ViewModels.Idea;
using Framework.Enums;
using Framework.Exceptions;
using Framework.Lists;
using Framework.Mvc;
using Framework.Mvc.Filters;
using Framework.Mvc.Mvc;
using Framework.Strings;
using Globalization.Resources.Idea;
using Globalization.Resources.Services;
using Globalization.Resources.Shared;
using Services.Enums;
using Services.Exceptions;
using Services.Infrastructure;
using Services.ModelServices;
using Services.Session;
using Web.Helpers;
using Authorize = Web.Infrastructure.Attributes.AuthorizeAttribute;
using CommentViews = Services.Enums.CommentViews;
using ObjectVisibility = Data.Enums.ObjectVisibility;

//Controller for a Voting
namespace Web.Controllers
{
    //[ValidateAntiForgeryTokenWrapper(HttpVerbs.Post)]
    public partial class IdeaController : SiteBaseServiceController<IdeaService>
    {
        private IdeaListViews? IdeaListView
        {
            get
            {
                var show = Session["IdeaListView"];
                if (show == null)
                {
                    return null;
                }

                return (IdeaListViews)show;
            }
            set
            {
                Session["IdeaListView"] = value;
            }
        }

        private IdeaListSorts? IdeaListSort
        {
            get
            {
                var show = Session["IdeaListSort"];
                if (show == null)
                {
                    return null;
                }

                return (IdeaListSorts)show;
            }
            set
            {
                Session["IdeaListSort"] = value;
            }
        }

        private CommentViews? CommentsSort
        {
            get
            {
                var show = Session["CommentsSort"];
                if (show == null)
                {
                    return null;
                }

                return (CommentViews)show;
            }
            set
            {
                Session["CommentsSort"] = value;
            }
        }

        private ForAgainst? CommentsFilter
        {
            get
            {
                var show = Session["CommentsFilter"];
                if (show == null)
                {
                    return null;
                }

                return (ForAgainst)show;
            }
            set
            {
                Session["CommentsFilter"] = value;
            }
        }

        private IEnumerable<int> SelectedCategories
        {
            get { return (IEnumerable<int>)Session["SelectedCategories"]; }
            set { Session["SelectedCategories"] = value; }
        }

        private IEnumerable<string> SelectedStates
        {
            get { return (IEnumerable<string>)Session["SelectedStates"]; }
            set { Session["SelectedStates"] = value; }
        }

        private List<SimpleListModel> IdeasList
        {
            get
            {
                if (Session["IdeasList"] == null)
                {
                    Session["IdeasList"] = Service.GetIdeasList(GetIdeaListView(null), GetIdeaListSort(null), SelectedCategories, SelectedStates, OrganizationId);
                }

                return (List<SimpleListModel>)Session["IdeasList"];
            }
            set { Session["IdeasList"] = value; }
        }

        private IdeaCreateEditModel TempModel
        {
            get
            {
                return TempData["IdeaModel"] as IdeaCreateEditModel;
            }
            set { TempData["IdeaModel"] = value; }
        }

        public IdeaController()
        {
        }

        [HttpGet]
        public virtual ActionResult FinishedIndex()
        {
            SelectedStates = Service.GetFinishedStatesList();
            return Index(IdeaListViews.Interesting, IdeaListSorts.MostActive, null);
        }

        [HttpGet]
        public virtual ActionResult ActiveIndex()
        {
            SelectedStates = Service.GetActiveStatesList();
            return Index(IdeaListViews.Interesting, IdeaListSorts.MostActive, null);
        }

        public virtual ActionResult SendNotification(string ideaId)
        {
            return Json(Service.SendNotification(ideaId));
        }

        public virtual ActionResult GetFinishedIdeasDashboard()
        {
            if (!IsAjaxRequest)
            {
                return RedirectToAction(MVC.NewsFeed.Default(CurrentUser.LanguageCode));
            }
            var model = Service.GetTopFinishedIdeas(0);
            return Json(new { Content = RenderPartialViewToString(MVC.NewsFeed.Views.DashboardFinishedIdeas, model) });
        }

        public virtual ActionResult GetActiveIdeasDashboard()
        {
            if (!IsAjaxRequest)
            {
                return RedirectToAction(MVC.NewsFeed.Default(CurrentUser.LanguageCode));
            }
            var model = Service.GetTopResolvedIdeas(0);
            return Json(new { Content = RenderPartialViewToString(MVC.NewsFeed.Views.DashboardIdeas, model) });
        }

        [HttpGet]
        [ImportModelStateFromTempData]
        public virtual ActionResult Index(IdeaListViews? ideaListView, IdeaListSorts? ideaListSort, string organizationId, int? categoryId = null)
        {
            OrganizationId = organizationId;
            if (categoryId.HasValue)
            {
                this.SelectedCategories = new int[] { categoryId.Value };
            }

            var ideas = Service.GetIdeaPage(0, GetIdeaListView(ideaListView), GetIdeaListSort(ideaListSort), SelectedCategories, organizationId, SelectedStates);
            ideas.IdeaListView = (int)GetIdeaListView(ideaListView);
            ideas.IdeaListSort = (int)GetIdeaListSort(ideaListSort);
            IdeasList = null;

            if (IsTour)
            {
                if (ideas.Items.List.Any())
                {
                    Tour(MVC.Idea.Details(ideas.Items.List.First().Id, ideas.Items.List.First().Subject.ToSeoUrl(), null), Resource.TourIdeas);
                }
                else
                {
                    Tour(MVC.Voting.Index(), Resource.TourIdeas);
                }
            }

            if (IsJsonRequest)
            {
                foreach (var idea in ideas.Items.List)
                {
                    idea.Versions = null;
                }

                return Jsonp(ideas);
            }

            LastListUrl = Request.Url;
            return View(MVC.Idea.Views._Index, ideas);
        }


        public virtual ActionResult GetNextPage(int? pageIndex, string organizationId)
        {
            if (Request.HttpMethod == "GET")
            {
                return RedirectToAction(MVC.Idea.Index());
            }

            if (!pageIndex.HasValue)
            {
                return Json(null);
            }

            var ideas = Service.GetIdeaPage(pageIndex.Value, GetIdeaListView(IdeaListView), GetIdeaListSort(IdeaListSort), SelectedCategories, organizationId, SelectedStates);

            var json =
                new { Content = RenderPartialViewToString(MVC.Idea.Views.List, ideas.Items.List), ideas.Items.HasMoreElements, TotalCount = ideas.TotalCount };
            return Json(json);
        }

        public virtual ActionResult FilterPage(List<int> selectedCategoryIds, List<string> selectedStateIds)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Idea.Index());
            }

            SelectedCategories = selectedCategoryIds;
            SelectedStates = selectedStateIds;
            return GetNextPage(0, null);
        }

        /// <summary>
        /// Creates this instance.
        /// </summary>
        /// <returns></returns>
        [HttpGet, ImportModelStateFromTempData, Authorize]
        public virtual ActionResult Create(string organizationId = null, string relatedIdeaId = null, string problemId = null, string issueId = null)
        {
            if (TempModel != null)
            {
                return View(MVC.Idea.Views._Create, TempModel);
            }

            if (!EnsureIsUnique())
            {
                return RedirectToAction(MVC.Account.Details());
            }

            var model = Service.Create(relatedIdeaId, problemId, organizationId, issueId);

            return View(MVC.Idea.Views._Create, model);
        }

        [Authorize]
        public virtual ActionResult Publish(string ideaId)
        {
            var model = Service.Publish(ideaId);
            return Details(ideaId, null, null);
        }

        /// <summary>
        /// Creates new user.
        /// </summary>
        /// <param name="model">User to create.</param>
        /// <returns>The list view.</returns>
        [HttpPost, ExportModelStateToTempData, Authorize]
        public virtual ActionResult Create(IdeaCreateEditModel model)
        {
            if (model.IsPrivateToOrganization && string.IsNullOrEmpty(model.OrganizationId))
            {
                ModelState.AddModelError("IsPrivateToOrganization", Resource.SelectOrganization);
            }

            if (WorkContext.CategoryIds == null && (model.CategoryIds == null || !model.CategoryIds.Any()))
            {
                ModelState.AddModelError("CategoryIds", Resource.CategoryIdsIsEmpty);
            }


            //EnsureIsUnique();

            if (ModelState.IsValid)
            {
                var id = Service.Insert(model);
                if (!string.IsNullOrEmpty(id))
                {
                    if (!model.IsDraft)
                    {
                        TempData["Saved"] = true;
                    }
                    return
                        RedirectToSuccessAction(model.IsPrivateToOrganization
                                                    ? MVC.Organization.Details(model.OrganizationId,
                                                                               model.OrganizationName,
                                                                               OrganizationViews.Ideas, null, null, null)
                                                    : MVC.Idea.Details(id, model.Subject.ToSeoUrl(), null));
                }
            }

            model = Service.FillCreateEditModel(model);

            return View(MVC.Idea.Views._Create, model);
        }

        [Authorize]
        public virtual ActionResult SaveTempModel(IdeaCreateEditModel model)
        {
            TempModel = Service.FillCreateEditModel(model);
            return Json(true);
        }

        /// <summary>
        /// Creates this instance.
        /// </summary>
        /// <returns></returns>
        [HttpGet, ImportModelStateFromTempData, Authorize]
        public virtual ActionResult Edit(string id)
        {
            if (TempModel != null)
            {
                return View(MVC.Idea.Views._Create, TempModel);
            }

            var model = Service.GetIdeaForEdit(id);

            EnsureIsCreator(model);

            return View(MVC.Idea.Views._Create, model);
        }

        [HttpPost, ExportModelStateToTempData, Authorize]
        public virtual ActionResult Edit(IdeaCreateEditModel model)
        {
            if (ModelState.IsValid)
            {
                var publish = Service.Edit(model);
                if (publish)
                {
                    TempData["Saved"] = true;
                }

                return RedirectToAction(MVC.Idea.Details(model.Id, model.Subject.ToSeoUrl(), null));
            }

            model = Service.FillCreateEditModel(model);

            return View(MVC.Idea.Views._Create, model);
        }

        [HttpGet]
        [ImportModelStateFromTempData]
        public virtual ActionResult Details(string id, string subject, string versionId)
        {
            if (string.IsNullOrEmpty(id))
            {
                return RedirectToAction(MVC.Idea.Index());
            }

            try
            {
                var idea = Service.GetIdeaView(id, versionId);
                if (idea.Visibility == ObjectVisibility.Private &&
                    (CurrentUser.Organizations == null ||
                     !CurrentUser.IsUserInOrganization(idea.OrganizationId)))
                {
                    if (CurrentUser.IsAuthenticated)
                    {
                        return
                            RedirectToFailureAction(
                                MVC.Organization.Details(idea.OrganizationId, null, null, null, null, null),
                                Errors.PageNotPublic);
                    }

                    throw new UnauthorizedAccessException(
                        string.Format(
                            "UnauthorizedAccessException: You don't have access. You're currently logged in as {0}.",
                            CurrentUser.FullName));
                }

                // make sure the productName for the route matches the encoded product name
                string expectedName = idea.Subject.ToSeoUrl();
                string actualName = (subject ?? "").ToLower();

                // permanently redirect to the correct URL
                if (expectedName != actualName)
                {
                    return RedirectToActionPermanent("Details", "Idea",
                                                     new { id = idea.Id, subject = expectedName, versionId = versionId });
                }

                if (IsTour)
                {
                    Tour(MVC.Voting.Index(),
                         Resource.TourIdea);
                }

                if (IsJsonRequest)
                {
                    return Jsonp(idea);
                }

                return View(MVC.Idea.Views._Details, idea);
            }
            catch (ObjectNotFoundException ex)
            {
                return HttpNotFound();
            }
        }

        public virtual ActionResult GetVersion(string id, string versionId)
        {
            if (!Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Idea.Details(id, null, versionId));
            }

            var version = Service.GetVersion(id, versionId);
            return Json(new
            {
                Text = version.CurrentVersion.Text,
                Subject = version.CurrentVersion.Subject,
                VersionId = version.CurrentVersion.Id,
                Editable = version.CurrentVersion.IsEditable,
                UserFullName = string.IsNullOrEmpty(version.CurrentVersion.OrganizationName) ?
                    version.CurrentVersion.CreatorFullName : version.CurrentVersion.OrganizationName,
                UserProfileLink = string.IsNullOrEmpty(version.CurrentVersion.OrganizationId) ?
                    Url.Action(MVC.Account.Details(version.CurrentVersion.CreatorObjectId, version.CurrentVersion.CreatorFullName.ToSeoUrl(), null)) :
                    Url.Action(MVC.Organization.Details(version.CurrentVersion.OrganizationId, version.CurrentVersion.OrganizationName.ToSeoUrl(), null, null, null, null)),
                CreatedOn = version.CurrentVersion.CreatedOn.ToLongDateString(),
                VotingStatistics = !version.IsClosed && version.State != IdeaStates.Resolved ? RenderPartialViewToString(MVC.Idea.Views.VotingStatistics, version) : null,
                //VotingButtons = !version.IsClosed && version.State != IdeaStates.Resolved ? RenderPartialViewToString(MVC.Idea.Views.VotingButtons, version) : null,
                Documents = version.CurrentVersion.Attachments.Any() ? RenderPartialViewToString(MVC.Google.Views.DocsList, version.CurrentVersion.Attachments) : null
            });
        }

        [Authorize]
        public virtual ActionResult AddVersion(IdeaViewModel model)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Idea.Details(model.Id, null, null));
            }

            if (ModelState.IsValid)
            {
                var idea = Service.AddNewVersion(model);
                return Json(new
                                {
                                    Text = model.CurrentVersion.Text,
                                    VersionId = idea.CurrentVersion.Id,
                                    Versions = RenderPartialViewToString(MVC.Idea.Views._Versions, idea),
                                    VotingStatistics = RenderPartialViewToString(MVC.Idea.Views.VotingStatistics, idea),
                                    VotingButtons = RenderPartialViewToString(MVC.Idea.Views.VotingButtons, idea),
                                    Editable = true
                                });
            }

            return Json(new
            {
                error = (from v in ModelState.Values
                         from e in v.Errors
                         select e.ErrorMessage).Concatenate(";")
            });
        }

        [Authorize]
        public virtual ActionResult DeleteVersion(string ideaId, string versionId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Idea.Details(ideaId, null, versionId));
            }

            var success = Service.DeleteVersion(ideaId, versionId);
            return Json(success);
        }

        [Authorize]
        public virtual ActionResult AddComment(CommentView model, EmbedModel embed)
        {
            if (!IsAjaxRequest)
            {
                return RedirectToAction(MVC.Idea.Details(model.EntryId, null, model.VersionId));
            }

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

                    return Jsonp(new
                    {
                        Comment = RenderPartialViewToString(MVC.Comments.Views._Comment, comment),
                        SubscribeMain = RenderPartialViewToString(MVC.Shared.Views.Subscribe, comment.SubscribeMain)
                    });
                }
                catch (Exception ex)
                {
                    return ProcessError(ex);
                }
            }

            return Jsonp(false);
        }

        [Authorize]
        public virtual ActionResult AddCommentComment(CommentView model, EmbedModel embed)
        {
            if (!IsAjaxRequest)
            {
                return RedirectToAction(MVC.Idea.Details(model.EntryId, null, null));
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

                    return Jsonp(new
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

            return Jsonp(false);
        }

        [Authorize]
        public virtual ActionResult DeleteComment(string id, string commentId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Idea.Details(id, null, null));
            }

            var success = Service.DeleteComment(id, commentId);
            return Json(success);
        }

        [Authorize]
        public virtual ActionResult DeleteCommentComment(string id, string commentId, string commentCommentId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Idea.Details(id, null, null));
            }

            var success = Service.DeleteCommentComment(id, commentId, commentCommentId);
            return Json(success);
        }

        public virtual ActionResult GetComments(string id, CommentViews? sort, ForAgainst? filter, string versionId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Idea.Details(id, null, versionId));
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
            else if (sort == CommentViews.MostRecent)
            {
                comments = Service.GetCommentsMostRecent(id, 0, filter);
            }
            else if (sort == CommentViews.ByVersion)
            {
                comments = Service.GetCommentsByVersion(id, versionId, 0, filter);
            }

            var json =
                new
                {
                    Content = RenderPartialViewToString(MVC.Comments.Views._CommentList, comments.List),
                    comments.HasMoreElements,
                    UpdatedHref = Url.Action(MVC.Idea.GetMoreComments(id, null, sort, filter, versionId))
                };
            return Json(json);
        }

        public virtual ActionResult GetMoreComments(string id, int? pageIndex, CommentViews? sort, ForAgainst? filter, string versionId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Idea.Details(id, null, versionId));
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
            else if (sort == CommentViews.MostRecent)
            {
                comments = Service.GetCommentsMostRecent(id, pageIndex.Value, filter);
            }
            else if (sort == CommentViews.ByVersion)
            {
                comments = Service.GetCommentsByVersion(id, versionId, pageIndex.Value, filter);
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
                return RedirectToAction(MVC.Idea.Details(id, null, null));
            }

            var result = Service.LikeComment(id, commentId, parentId);
            return Json(new { Content = RenderPartialViewToString(MVC.Comments.Views.Like, result) });
        }

        [Authorize]
        public virtual ActionResult UndoLikeComment(string id, string commentId, string parentId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Idea.Details(id, null, null));
            }

            var result = Service.UndoLikeComment(id, commentId, parentId);
            return Json(new { Content = RenderPartialViewToString(MVC.Comments.Views.Like, result) });
        }

        [Authorize(true)]
        public virtual ActionResult Vote(string id, string versionId)
        {
            try
            {
                if (IsAjaxRequest)
                {
                    LastVoteReferrerUrl = Request.UrlReferrer;
                }

                var model = Service.Vote(id, versionId);

                if (!IsAjaxRequest)
                {
                    if (model == null)
                    {
                        return RedirectToFailureAction(MVC.Idea.Details(id, null, null), Resource.CannotVoteClosedIdea);
                    }

                    if (!model.WasLiked)
                    {
                        TempData["VoteSuccess"] = Url.ToAbsoluteUrl(model.ShortLink);

                        return RedirectToAction(MVC.NewsFeed.Default(CurrentUser.LanguageCode));
                    }
                    else
                    {
                        return RedirectToAction(MVC.Idea.Details(id, model.Subject.ToSeoUrl(), null));
                    }
                }

                if (model == null)
                {
                    return Json(false);
                }

                return Jsonp(new
                    {
                        VotingStatistics = RenderPartialViewToString(MVC.Idea.Views.VotingStatistics, model),
                        VotingButtons = RenderPartialViewToString(MVC.Idea.Views.VotingButtons, model),
                        Versions = RenderPartialViewToString(MVC.Idea.Views._Versions, model),
                        Type = EntryTypes.Idea.ToString(),
                        VersionId = versionId,
                        Progress =
                                 model.Progress != null
                                     ? RenderPartialViewToString(MVC.Idea.Views.Progress, model.Progress)
                                     : null,
                        Subscribe = RenderPartialViewToString(MVC.Shared.Views.Subscribe, model.Subscribe)
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
                            Content =
                                RenderPartialViewToString(MVC.Account.Views.AdditionalUniqueInfoContents,
                                                          ex.AdditionalInfo),
                            Title = Globalization.Resources.Account.Resource.AdditionalUniqueInfoTitle
                        };

                    return result;
                }

                TempData["OpenAdditinalUniqueInfoDialog"] = ex.AdditionalInfo;
                return Details(id, null, null);
            }
            catch (UserNotUniqueViispException ex)
            {
                if (IsAjaxRequest)
                {
                    return ProcessError(ex, false);
                }

                TempData["OpenViispDialog"] = true;
                return Details(id, null, null);
            }
            catch (UserCannotSignException e)
            {
                if (IsAjaxRequest)
                {
                    return ProcessError(new Exception(Resource.CannotSign, e), true, false);
                }

                return RedirectToFailureAction(MVC.Idea.Details(id, null, null),
                                               Resource.CannotSign, true);
            }
            catch (Exception e)
            {
                return ProcessError(e, false);
            }
        }

        public virtual ActionResult GetVersions(string id)
        {
            if (!IsAjaxRequest)
            {
                return RedirectToAction(MVC.Idea.Index());
            }

            var model = Service.GetIdeaView(id, null);

            ViewBag.IsInline = true;

            return Jsonp(new
            {
                Content = RenderPartialViewToString(MVC.Idea.Views._Versions, model),
                VersionInput = RenderPartialViewToString(MVC.Idea.Views.VersionInput, model)
            });
        }

        [Authorize(true)]
        public virtual ActionResult CancelVote(string id, string versionId)
        {
            if (!IsAjaxRequest)
            {
                return RedirectToAction(MVC.Idea.Details(id, null, versionId));
            }

            var model = Service.CancelVote(id, versionId);
            return Jsonp(new
            {
                VotingStatistics = RenderPartialViewToString(MVC.Idea.Views.VotingStatistics, model),
                VotingButtons = RenderPartialViewToString(MVC.Idea.Views.VotingButtons, model),
                Versions = RenderPartialViewToString(MVC.Idea.Views._Versions, model),
                Type = EntryTypes.Idea.ToString(),
                VersionId = versionId
            });
        }

        public virtual ActionResult GetCreatedIdeas(string userObjectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            var model = Service.GetCreatedIdeas(userObjectId, 0);
            model.ActionResult = MVC.Idea.GetNextCreatedIdeasPage(userObjectId, null);
            var json =
                new { Content = RenderPartialViewToString(MVC.Shared.Views.SimpleListContainer, model) };
            return Json(json);
        }

        [AjaxOnly]
        public virtual ActionResult GetNextCreatedIdeasPage(string userObjectId, int? pageIndex)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            if (!pageIndex.HasValue)
            {
                return Json(null);
            }

            var issues = Service.GetCreatedIdeas(userObjectId, pageIndex.Value);

            var json =
                new { Content = RenderPartialViewToString(MVC.Shared.Views.SimpleList, issues.List.List), issues.List.HasMoreElements };
            return Json(json);
        }

        public virtual ActionResult GetInvolvedIdeas(string userObjectId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            var model = Service.GetInvolvedIdeas(userObjectId, 0);
            model.ActionResult = MVC.Idea.GetNextInvolvedIdeasPage(userObjectId, null);
            var json =
                new { Content = RenderPartialViewToString(MVC.Shared.Views.SimpleListContainer, model) };
            return Json(json);
        }

        public virtual ActionResult GetNextInvolvedIdeasPage(string userObjectId, int? pageIndex)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Account.Details(userObjectId, null, null));
            }

            if (!pageIndex.HasValue)
            {
                return Json(null);
            }

            var issues = Service.GetInvolvedIdeas(userObjectId, pageIndex.Value);

            var json =
                new { Content = RenderPartialViewToString(MVC.Shared.Views.SimpleList, issues.List.List), issues.List.HasMoreElements };
            return Json(json);
        }

        public virtual ActionResult GetSupporters(string ideaId, string versionId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Idea.Details(ideaId, null, versionId));
            }

            var model = Service.GetSupporters(ideaId, versionId, 0);
            model.ActionResult = MVC.Idea.GetNextGetSupportersPage(ideaId, versionId, null);
            var json =
                new { Content = RenderPartialViewToString(MVC.Shared.Views.UserListContainer, model) };
            return Json(json);
        }

        public virtual ActionResult GetNextGetSupportersPage(string ideaId, string versionId, int? pageIndex)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Idea.Details(ideaId, null, versionId));
            }

            if (!pageIndex.HasValue)
            {
                return Json(null);
            }

            var issues = Service.GetSupporters(ideaId, versionId, pageIndex.Value);

            var json =
                new { Content = RenderPartialViewToString(MVC.Shared.Views.UserList, issues.List.List), issues.List.HasMoreElements };
            return Json(json);
        }

        public virtual ActionResult GetUniqueSupporters(string ideaId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Idea.Details(ideaId, null, null));
            }

            var model = Service.GetUniqueSupporters(ideaId, 0);
            model.ActionResult = MVC.Idea.GetNextGetUniqueSupportersPage(ideaId, null);
            var json =
                new { Content = RenderPartialViewToString(MVC.Shared.Views.UserListContainer, model) };
            return Json(json);
        }

        public virtual ActionResult GetNextGetUniqueSupportersPage(string ideaId, int? pageIndex)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Idea.Details(ideaId, null, null));
            }

            if (!pageIndex.HasValue)
            {
                return Json(null);
            }

            var issues = Service.GetUniqueSupporters(ideaId, pageIndex.Value);
            var json =
                new { Content = RenderPartialViewToString(MVC.Shared.Views.UserList, issues.List.List), issues.List.HasMoreElements };
            return Json(json);
        }

        [HttpGet]
        public virtual ActionResult Prioritizer()
        {
            if (!IsAuthenticated)
            {
                return PrioritizerResults();
            }

            var model = Service.GetIdeasForPrioritizer();
            return View(MVC.Idea.Views.Prioritizer, model);
        }

        [HttpPost, Authorize]
        public virtual ActionResult Prioritizer(PrioritizerModel model)
        {
            var result = Service.SavePrioritizedIdeas(model);
            return View(MVC.Idea.Views.PrioritizerResult, result);
        }

        [HttpGet]
        public virtual ActionResult PrioritizerResults()
        {
            var result = Service.GetPrioritizedIdeas();
            return View(MVC.Idea.Views.PrioritizerResult, result);
        }

        [Authorize]
        public virtual ActionResult SaveMyCategories(IList<int> selectedCategoryIds)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Idea.Index());
            }

            Service.LikeCategories(selectedCategoryIds);

            return Json(true);
        }

        [Authorize]
        public virtual ActionResult JoinIdeaRealization(string ideaId)
        {
            var projectId = Service.Join(ideaId);
            return RedirectToAction(MVC.Project.Team(projectId));
        }

        [Authorize]
        public virtual ActionResult ChangeState(string ideaId, IdeaStates nextStateId)
        {
            Service.ChangeState(ideaId, nextStateId);
            return RedirectToAction(MVC.Idea.Details(ideaId, null, null));
        }

        [ValidateInput(false), Authorize]
        public virtual ActionResult CloseIdea(string ideaId, string closingReason, bool depersonate)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Idea.Details(ideaId, null, null));
            }

            Service.ChangeState(ideaId, IdeaStates.Closed, closingReason, depersonate);
            return Json(true);
        }

        [ValidateInput(false), Authorize]
        public virtual ActionResult IdeaRealized(string ideaId, string stateDescription)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Idea.Details(ideaId, null, null));
            }

            if (string.IsNullOrEmpty(stateDescription))
            {
                Service.ChangeState(ideaId, IdeaStates.New, stateDescription);
            }
            else
            {
                Service.ChangeState(ideaId, IdeaStates.Realized, stateDescription);
            }

            return Json(true);
        }

        [Authorize]
        public virtual ActionResult OpenIdea(string ideaId)
        {
            if (!Service.CanCurrentUserEdit(ideaId))
            {
                return new HttpUnauthorizedResult();
            }

            Service.ChangeState(ideaId, IdeaStates.New);
            return RedirectToAction(MVC.Idea.Details(ideaId, null, null));
        }

        [Authorize]
        public virtual ActionResult ResolveIdea(FinalIdeaModel model)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Idea.Details(model.IdeaId, null, null));
            }

            if (!model.Cancel && string.IsNullOrEmpty(model.Resolution) && string.IsNullOrEmpty(model.FinalVersionId))
            {
                return Json(new { resolveError = Resource.ResolveError });
            }


            if (ModelState.IsValid)
            {
                try
                {
                    if (Service.ResolveIdea(model))
                    {
                        return Json(true);
                    }
                    else
                    {
                        return Json(new { resolveError = Resource.CannotResolve });
                    }
                }
                catch (UserNotUniqueException ex)
                {
                    if (IsAjaxRequest)
                    {
                        return ProcessError(ex);
                    }

                    TempData["OpenConfirmIdentityDialog"] = true;
                    return Details(model.IdeaId, null, null);
                }
                catch (Exception e)
                {
                    return ProcessError(e);
                }
            }

            return Json(false);
        }

        [Authorize]
        public virtual ActionResult AddRelatedIdea(string id, string name)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Idea.Edit(id));
            }

            return Json(new { Content = RenderPartialViewToString(MVC.Idea.Views.RelatedIdea, new RelatedIdeaListItem() { ObjectId = id, Name = name, IsDeletable = true }) });
        }

        [Authorize]
        public virtual ActionResult AddRelatedIssue(string id, string name)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Idea.Edit(id));
            }

            return Json(new { Content = RenderPartialViewToString(MVC.Idea.Views.RelatedIssue, new ListItem() { ObjectId = id, Name = name, IsDeletable = true }) });
        }

        [Authorize]
        public virtual ActionResult GetAllIdeas(string id)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Idea.Create());
            }

            return Json(Service.GetAllIdeasSelectList(id));
        }

        [Authorize]
        public virtual ActionResult GetMatchedIdeas(string prefix)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Problem.Index());
            }

            var model = Service.GetMatchedIdeas(prefix);
            return Json(model);
        }

        public virtual ActionResult UpdateDb()
        {
            if (!Service.UpdateIdeaDb())
            {
                throw new Exception("Failed updating idea DB");
            }

            return RedirectToAction(MVC.Common.Start());
        }

        public virtual ActionResult UpdateCommentNumbers()
        {
            if (!Service.UpdateCommentNumbers())
            {
                throw new Exception("Failed updating comment numbers");
            }

            return RedirectToAction(MVC.Common.Start());
        }

        public virtual ActionResult SetIsPublicUnique(string ideaId, string personCode, bool isPublic)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Idea.Details(ideaId, null, null));
            }

            return Json(Service.SetIsPublicUnique(ideaId, personCode, isPublic));
        }

        public virtual ActionResult SetIsPublic(string ideaId, string versionId, string userObjectId, bool isPublic)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Idea.Details(ideaId, null, null));
            }

            return Json(Service.SetIsPublic(ideaId, versionId, userObjectId, isPublic));
        }

        private IdeaListViews GetIdeaListView(IdeaListViews? view)
        {
            if (view.HasValue)
            {
                IdeaListView = view.Value;
                return view.Value;
            }

            if (IdeaListView.HasValue)
            {
                return IdeaListView.Value;
            }

            return IdeaListViews.Interesting;
        }

        private IdeaListSorts GetIdeaListSort(IdeaListSorts? sort)
        {
            if (sort.HasValue)
            {
                IdeaListSort = sort.Value;
                return sort.Value;
            }

            if (IdeaListSort.HasValue)
            {
                return IdeaListSort.Value;
            }

            if (CurrentUser.IsAuthenticated)
            {
                return IdeaListSorts.MostRecent;
            }

            return IdeaListSorts.MostActive;
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

        private void EnsureIsCreator(IdeaCreateEditModel model)
        {
            if (!model.CanCurrentUserEdit)
            {
                var currentUserName = CurrentUser.FullName;

                throw new UnauthorizedAccessException(string.Format("UnauthorizedAccessException: You don't have access. You're currently logged in as {0}.",
                                                      currentUserName));
            }
        }

        public virtual ActionResult GetMoreFinishedIdeas(int? pageIndex)
        {
            var model = Service.GetTopFinishedIdeas(pageIndex ?? 0);
            return Json(new { Content = RenderPartialViewToString(MVC.NewsFeed.Views.FinishedIdeasList, model), model.Items.HasMoreElements });
        }

        public virtual ActionResult GetMoreResolvedIdeas(int? pageIndex)
        {
            var model = Service.GetTopResolvedIdeas(pageIndex ?? 0);
            return Json(new { Content = RenderPartialViewToString(MVC.NewsFeed.Views.IdeasList, model), model.Items.HasMoreElements });
        }

        public virtual ActionResult PreviousIdea(string id)
        {
            if (IdeasList == null || IdeasList.Count == 0)
            {
                return RedirectToAction(MVC.Voting.Details(id, null));
            }

            var current = IdeasList.SingleOrDefault(i => i.Id == id);
            var index = IdeasList.IndexOf(current) - 1;
            if (index < 0)
            {
                index = IdeasList.Count - 1;
            }

            return RedirectToAction(MVC.Idea.Details(IdeasList[index].Id, IdeasList[index].Subject.ToSeoUrl(), null));
        }

        public virtual ActionResult NextIdea(string id)
        {
            if (IdeasList == null || IdeasList.Count == 0)
            {
                return RedirectToAction(MVC.Voting.Details(id, null));
            }

            var current = IdeasList.SingleOrDefault(i => i.Id == id);
            var index = IdeasList.IndexOf(current) + 1;
            if (index >= IdeasList.Count)
            {
                index = 0;
            }

            if (IdeasList.Any())
            {
                return RedirectToAction(MVC.Idea.Details(IdeasList[index].Id, IdeasList[index].Subject.ToSeoUrl(), null));
            }

            return RedirectToAction(MVC.Idea.Details(id, null, null));
        }

        public virtual ActionResult MakePublic(string id)
        {
            Service.MakePublic(id);
            return RedirectToAction(MVC.Idea.Details(id, null, null));
        }

        public virtual ActionResult GetRelatedIdeaDialog(string id)
        {
            var model = Service.GetRelatedIdeaDialogModel(id);
            var json =
                new { Content = RenderPartialViewToString(MVC.Idea.Views.AddRelatedIdeaForm, model) };
            return Json(json);
        }

        public virtual ActionResult SaveRelatedIdeas(RelatedIdeaDialogModel model)
        {
            Service.SaveRelatedIdeas(model);
            return RedirectToAction(MVC.Idea.Details(model.Id, null, null));
        }

        public virtual ActionResult GetRelatedIssueDialog(string id)
        {
            var model = Service.GetRelatedIssueDialogModel(id);
            var json =
                new { Content = RenderPartialViewToString(MVC.Idea.Views.AddRelatedIssueForm, model) };
            return Json(json);
        }

        public virtual ActionResult SaveRelatedIssues(RelatedIssueDialogModel model)
        {
            Service.SaveRelatedIssues(model);
            return RedirectToAction(MVC.Idea.Details(model.Id, null, null));
        }

        public virtual ActionResult RevertDefaultCategories(string url)
        {
            SelectedCategories = null;
            if (!string.IsNullOrEmpty(url))
            {
                return Redirect(url);
            }

            return RedirectToAction(MVC.Common.Start());
        }

        public virtual ActionResult FixSupportingUserIds()
        {
            return RedirectToAction(MVC.Common.Start());
        }

        public virtual ActionResult GetText(string id, string versionId, string historyId)
        {
            if (Request.HttpMethod == "GET" || !Request.IsAjaxRequest())
            {
                return RedirectToAction(MVC.Idea.Details(id, null, versionId));
            }

            var version = Service.GetHistoryVersion(id, versionId, historyId);
            return Json(version);
        }

        public virtual ActionResult CompareHistoryVersions(string id, string versionId, string historyId1,
                                                           string historyId2)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(versionId) || string.IsNullOrEmpty(historyId1) ||
                string.IsNullOrEmpty(historyId2) || historyId1 == historyId2)
            {
                return Json(false);
            }

            var html = Service.CompareHistoryVersions(id, versionId, historyId1, historyId2);
            return Json(new { Content = html });
        }

        public virtual ActionResult PromoteToFrontPage(string id, bool promote)
        {
            if (CurrentUser.Role != UserRoles.Admin)
            {
                ModelState.AddModelError("", SharedStrings.Error);
                return RedirectToAction(MVC.Idea.Details(id, null, null));
            }

            Service.PromoteToFrontPage(id, promote);
            return RedirectToAction(MVC.Idea.Details(id, null, null));
        }
    }
}
