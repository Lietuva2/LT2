using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity.Core;
using System.Data.Entity.Validation;
using System.Linq;
using System.Transactions;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using Bus.Commands;
using Data.EF.Actions;
using Data.EF.Voting;
using Data.Enums;
using Data.Infrastructure.Sessions;
using Data.MongoDB;
using Data.MongoDB.Interfaces;
using Data.ViewModels.Account;
using Data.ViewModels.Base;
using Data.ViewModels.Comments;
using Data.ViewModels.Idea;
using Data.ViewModels.Problem;
using EntityFramework.Extensions;
using Framework;
using Framework.Enums;
using Framework.Exceptions;
using Framework.Hashing;
using Framework.Infrastructure;
using Framework.Infrastructure.Logging;
using Framework.Infrastructure.Storage;
using Framework.Infrastructure.ValueInjections;
using Framework.Lists;
using Framework.Mvc.Helpers;
using Framework.Mvc.Lists;
using Framework.Mvc.Strings;
using Framework.Other;
using Framework.Strings;
using Globalization;
using Globalization.Resources.Services;
using Helpers;
using MongoDB.Bson;
using MongoDB.Driver.Builders;

using Omu.ValueInjecter;
using Services.Caching;
using Services.Classes;
using Services.Enums;
using Services.Exceptions;
using Services.Infrastructure;
using Services.Session;
using Comment = Data.MongoDB.Comment;
using Idea = Data.MongoDB.Idea;
using Issue = Data.MongoDB.Issue;
using System.Threading.Tasks;
using Framework.Bus;
using ObjectVisibility = Data.Enums.ObjectVisibility;

namespace Services.ModelServices
{
    public class IdeaService : BaseCommentableService, IService
    {
        private readonly IVotingContextFactory votingSessionFactory;
        private readonly IActionsContextFactory actionSessionFactory;
        private readonly Func<INoSqlSession> noSqlSessionFactory;
        private readonly AddressService addressService;
        private readonly CategoryService categoryService;
        private readonly SearchService searchService;
        private readonly ShortLinkService shortLinkService;

        private readonly IBus bus;
        private readonly ICache cache;
        private readonly ILogger logger;

        public UserService UserService { get { return ServiceLocator.Resolve<UserService>(); } }
        public ProblemService ProblemService { get { return ServiceLocator.Resolve<ProblemService>(); } }
        public OrganizationService OrganizationService { get { return ServiceLocator.Resolve<OrganizationService>(); } }
        public ActionService ActionService { get { return ServiceLocator.Resolve<ActionService>(); } }

        public UrlHelper Url
        {
            get { return new UrlHelper(((MvcHandler)HttpContext.Current.Handler).RequestContext); }
        }

        public IdeaService(
            IVotingContextFactory votingSessionFactory,
            IActionsContextFactory actionSessionFactory,
            AddressService addressService,
            Func<INoSqlSession> noSqlSessionFactory,
            CategoryService categoryService,
            SearchService searchService,
            CommentService commentService,
            ShortLinkService shortLinkService,
            ICache cache,
            ILogger logger,
            IBus bus)
            : base(commentService)
        {
            this.votingSessionFactory = votingSessionFactory;
            this.noSqlSessionFactory = noSqlSessionFactory;
            this.actionSessionFactory = actionSessionFactory;
            this.categoryService = categoryService;
            this.addressService = addressService;
            this.bus = bus;
            if (categoryService != null)
            {
                categoryService.Bus = bus;
            }
            this.cache = cache;
            this.categoryService = categoryService;
            this.searchService = searchService;
            this.shortLinkService = shortLinkService;
            this.logger = logger;
        }

        public IQueryable<Idea> GetAllIdeas()
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                return noSqlSession.GetAll<Idea>();
            }
        }

        public IdeaIndexModel GetTopResolvedIdeas(int pageNumber)
        {
            return GetIdeaPage(pageNumber, CustomAppSettings.PageSizeStartPage, IdeaListViews.Interesting, IdeaListSorts.MostRecent, categoryService.GetCategories().Select(a => a.ValueInt), null, GetActiveStatesList(), true);
        }

        public IdeaIndexModel GetTopFinishedIdeas(int pageNumber)
        {
            return GetIdeaPage(pageNumber, CustomAppSettings.PageSizeStartPage, IdeaListViews.Interesting, IdeaListSorts.MostRecent, categoryService.GetCategories().Select(a => a.ValueInt), null, GetFinishedStatesList());
        }

        public List<string> GetActiveStatesList()
        {
            return new List<string>() { ToIntString(IdeaStates.Resolved) };
        }

        public List<string> GetFinishedStatesList()
        {
            return new List<string>()
                       {
                           ToIntString(IdeaStates.Realized),
                           ToIntString(IdeaStates.Voting),
                           ToIntString(IdeaStates.Rejected)
                       };
        }

        private string ToIntString(IdeaStates e)
        {
            return ((int)e).ToString();
        }

        public IdeaIndexModel GetIdeaPage(int pageNumber, IdeaListViews? view, IdeaListSorts sort, IEnumerable<int> selectedCategories, string organizationId, IEnumerable<string> selectedStates, bool isFrontPage = false)
        {
            return GetIdeaPage(pageNumber, CustomAppSettings.PageSizeList, view, sort, selectedCategories,
                               organizationId, selectedStates, isFrontPage);
        }

        public IdeaIndexModel GetIdeaPage(int pageNumber, int pageSize, IdeaListViews? view, IdeaListSorts sort, IEnumerable<int> selectedCategories, string organizationId, IEnumerable<string> selectedStates, bool isFrontPage = false)
        {
            EnsureIdeaIndices();
            IQueryable<Idea> query;

            var model = new IdeaIndexModel();

            if (WorkContext.CategoryIds != null)
            {
                selectedCategories = WorkContext.CategoryIds;
            }
            else
            {
                if (CurrentUser.IsAuthenticated && selectedCategories == null)
                {
                    selectedCategories = categoryService.GetMyCategoryIds();
                }
            }

            model.SelectedCategories = GetSelectedCategories(selectedCategories);

            if (string.IsNullOrEmpty(organizationId))
            {
                query = GetSelectedCategoriesQuery(model.SelectedCategories.Where(c => c.Selected));

                if (WorkContext.Municipality == null)
                {
                    var myMunicipalities = GetMyMunicipalities();
                    if (myMunicipalities.Any())
                    {
                        query =
                            query.Where(
                                q => !q.MunicipalityId.HasValue || myMunicipalities.Contains(q.MunicipalityId.Value));
                    }
                }

                model.SelectedStates = GetSelectedStates(selectedStates);

                if (view == IdeaListViews.Subscribed)
                {
                    var subscribedIdeaIds = UserService.GetSubscribedObjectIds(EntryTypes.Idea);
                    var ids = subscribedIdeaIds.Select(q => BsonObjectId.Create(q)).ToList();
                    using (var session = noSqlSessionFactory())
                    {
                        query = session.GetAllIn<Data.MongoDB.Idea>("_id", ids.ToArray());
                    }
                }
                else
                {
                    if (isFrontPage)
                    {
                        query =
                             query.Where(
                                 q =>
                                     model.SelectedStates.Where(s => s.Selected)
                                         .Select(s => s.Value)
                                         .Contains(((int)q.ActualState).ToString()) || q.PromoteToFrontPage); 
                    }
                    else
                    {
                        query =
                            query.Where(
                                q =>
                                    model.SelectedStates.Where(s => s.Selected)
                                        .Select(s => s.Value)
                                        .Contains(((int)q.ActualState).ToString()));
                    }

                    if (view == IdeaListViews.Other)
                    {
                        var ids = query.Select(q => BsonObjectId.Create(q.Id)).ToList();
                        ids.AddRange(UserService.GetSubscribedObjectIds(EntryTypes.Idea).Select(q => BsonObjectId.Create(q)).ToList());
                        using (var session = noSqlSessionFactory())
                        {
                            query = session.GetAllNotIn<Data.MongoDB.Idea>("_id", ids.ToArray());
                        }
                    }
                }

                if (WorkContext.Municipality != null)
                {
                    query = query.Where(q => q.MunicipalityId == WorkContext.Municipality.Id);
                }
            }
            else
            {
                query = GetAllIdeas().Where(i => i.OrganizationId == organizationId);
            }

            //query = query.Where(m => !m.IsPrivateToOrganization || CurrentUser.OrganizationIds.Contains(m.OrganizationId));

            if (!CurrentUser.OrganizationIds.Contains(organizationId))
            {
                query = query.Where(q => q.Visibility == ObjectVisibility.Public);
            }

            var userVotes = GetUserVotes();

            query = query.Where(m => !m.IsDraft || m.UserObjectId == CurrentUser.Id);

            model.TotalCount = query.Count();

            if (sort == IdeaListSorts.MostRecent)
            {
                if (isFrontPage)
                {
                    query = query.OrderBy(i => i.PromoteToFrontPage).ThenBy(i => IsSupportedByUniqueCurrentUser(userVotes, i.Id)).ThenByDescending(i => i.ModificationDate);
                }
                else
                {
                    query = query.OrderByDescending(i => i.ModificationDate).ThenByDescending(i => i.RegistrationDate);
                }
            }
            else if (sort == IdeaListSorts.MostActive)
            {
                if (isFrontPage)
                {
                    query =
                        query.OrderBy(i => i.PromoteToFrontPage).ThenBy(i => IsSupportedByUniqueCurrentUser(userVotes, i.Id)).ThenByDescending(i => i.VotesCount).ThenByDescending(
                            i => i.SummaryWiki.Versions.Sum(c => c.SupportingUserCount) * 2 + i.Comments.Count);
                }
                else
                {
                    query = query.ToList().AsQueryable();
                    query =
                        query.OrderByDescending(i => i.VotesCount).ThenByDescending(
                            i => i.SummaryWiki.Versions.Sum(c => c.SupportingUserCount) * 2 + i.Comments.Count);
                }
            }

            var result = query.GetExpandablePage(pageNumber, pageSize).Select(
                i => new IdeaIndexItemModel
                         {
                             Id = i.Id,
                             Subject = i.Subject,
                             CategoryIds = i.CategoryIds,
                             CommentsCount = i.Comments.Count,
                             ViewsCount = i.ViewsCount,
                             VersionsCount = i.SummaryWiki.Versions.Count,
                             Versions = i.SummaryWiki.Versions.Select(v => new WikiVersionModel()
                                 {
                                     Id = v.Id,
                                     Text = v.Text,
                                     CreatedOn = v.CreatedOn,
                                     CreatorFullName = v.CreatorFullName,
                                     CreatorObjectId = v.CreatorObjectId,
                                     Number = v.Number,
                                     OrganizationId = v.OrganizationId,
                                     OrganizationName = v.OrganizationName,
                                     Subject = v.Subject,
                                     SupportingUserCount = v.SupportingUserCount,
                                     SupportingUsers = v.SupportingUsers,
                                     SupportingConfirmedUserCount = v.SupportingUsers.Count(u => !string.IsNullOrEmpty(u.PersonCode)),
                                     SupportingUnconfirmedUserCount = v.SupportingUsers.Count(u => string.IsNullOrEmpty(u.PersonCode))
                                 }).ToList(),
                             InitiativeType = i.InitiativeType,
                             IsClosed = i.IsClosed,
                             State = i.ActualState,
                             Municipality = addressService.GetMunicipality(i.MunicipalityId),
                             AbsoluteUrl = Url.ActionAbsolute("Details", "Idea", new { id = i.Id, subject = i.Subject.ToSeoUrl() }),
                             IsDraft = i.IsDraft,
                             IsPrivate = i.IsPrivateToOrganization,
                             Deadline = i.Deadline,
                             Progress = i.RequiredVotes.HasValue ? new ProgressViewModel()
                                            {
                                                Id = i.Id,
                                                RequiredVotes = i.RequiredVotes.Value
                                            } : null,
                             Text = GetShortText(i)
                         }).ToList();

            foreach (var r in result)
            {
                r.Categories = r.CategoryIds.Select(cId => new TextValue() { Text = categoryService.GetCategoryName(cId), ValueInt = cId }).ToList();
                r.TotalSupporters = r.Versions.Sum(c => c.SupportingUserCount);
                r.TotalConfirmedSupporters = r.Versions.Sum(c => c.SupportingConfirmedUserCount);
                r.TotalUnconfirmedSupporters = r.Versions.Sum(c => c.SupportingUnconfirmedUserCount);
                r.ActivityRank = r.Versions.Sum(c => c.SupportingUserCount) * 2 + r.CommentsCount;
                r.IsBold = GetIsAvailableForVoting(r);
                if (r.Progress != null)
                {
                    r.Progress.NumberSupporters = GetUniqueSupportersCount(r.Id);
                    r.Progress.SupportPercentage = GetSupportPercentage(r.Progress.NumberSupporters,
                                                                        r.Progress.RequiredVotes);
                }
                r.SupportedByCurrentUser = IsSupportedByUniqueCurrentUser(userVotes, r.Id);
            }

            //if (sort == IdeaListSorts.MostActive && !notVotedOnly)
            //{
            //    result = result.OrderByDescending(i => i.ActivityRank).ToList();
            //}
            model.IsEditable = CurrentUser.IsAuthenticated;
            model.Items = new ExpandableList<IdeaIndexItemModel>(result, pageSize);

            return model;
        }

        private List<Data.EF.Voting.IdeaVote> GetUserVotes()
        {
            using (var session = votingSessionFactory.CreateContext())
            {
                return session.IdeaVotes.Where(i => i.PersonCode == CurrentUser.PersonCode).ToList();
            }
        }

        private string GetShortText(Idea idea)
        {
            string text = string.Empty;
            if (!string.IsNullOrEmpty(idea.FinalVersionId))
            {
                text = GetCurrentVersion(idea).Text.GetPlainText(1000).Trim();
            }

            if (string.IsNullOrEmpty(text))
            {
                text = idea.Aim;
                text = text.GetPlainText(1000).Trim();
            }

            if (string.IsNullOrEmpty(text))
            {
                text = idea.Resolution.GetPlainText(1000).Trim();
            }

            if (string.IsNullOrEmpty(text))
            {
                text = GetCurrentVersion(idea).Text;
                text = text.GetPlainText(1000).Trim();
            }

            return text;
        }

        private bool IsSupportedByUniqueCurrentUser(List<Data.EF.Voting.IdeaVote> votes, string ideaId)
        {
            return votes.Any(v => v.IdeaId == ideaId);
        }

        private bool IsSupportedByUniqueCurrentUser(string ideaId)
        {
            using (var session = votingSessionFactory.CreateContext())
            {
                return session.IdeaVotes.Any(i => i.IdeaId == ideaId && i.PersonCode == CurrentUser.PersonCode);
            }
        }

        private bool GetIsAvailableForVoting(IdeaIndexItemModel ideaModel)
        {
            if (!CurrentUser.IsAuthenticated)
            {
                return false;
            }

            if (ideaModel.IsDraft)
            {
                return false;
            }

            if (ideaModel.IsClosed)
            {
                return false;
            }

            if (ideaModel.Versions.Count == 1)
            {
                if (ideaModel.Versions.First().SupportingUsers.Select(u => u.Id).Contains(CurrentUser.Id))
                {
                    return false;
                }

                return true;
            }

            if (!ideaModel.Versions.Any(v => v.SupportingUsers.Select(u => u.Id).Contains(CurrentUser.Id)))
            {
                return true;
            }

            List<Data.EF.Voting.IdeaVersionView> viewedVersions = null;
            using (var votingSession = votingSessionFactory.CreateContext())
            {
                viewedVersions = (from v in votingSession.IdeaVersionViews
                                  where v.IdeaVersion.IdeaId == ideaModel.Id && v.UserDbId == CurrentUser.DbId
                                  select v).Distinct().ToList();
            }

            foreach (var version in ideaModel.Versions)
            {
                if (!GetIsViewedByCurrentUser(version.CreatorObjectId, version.Id, viewedVersions))
                {
                    return true;
                }
            }

            return false;
        }

        public IdeaCreateEditModel GetIdeaForEdit(MongoObjectId id)
        {
            var idea = GetIdea(id);
            var view = new IdeaCreateEditModel();
            view.InjectFrom<UniversalInjection>(idea);
            view.Subject = idea.Subject.HtmlDecode();
            view.Summary = idea.LastSummary;
            view.CanCurrentUserEdit = CanCurrentUserEdit(idea);
            view.CategoryIds = idea.CategoryIds;
            view.Categories = categoryService.GetCategories().ToSelectList();
            view.Municipalities = addressService.GetMunicipalities(1);
            view.OrganizationName = OrganizationService.GetOrganizationName(view.OrganizationId);
            view.RelatedIdeas = GetRelatedIdeas(id);
            view.RelatedIssues = GetRelatedIssues(id);
            view.Ideas = GetMyIdeasSelectList(id, idea.CategoryIds);
            view.Problems = GetIdeaProblems(id);
            view.IsMailSendable = IsMailSendable(idea, true);

            foreach (var item in idea.Urls)
            {
                var e = new UrlEditModel();
                e.InjectFrom<UniversalInjection>(item);
                view.Urls.Add(e);
            }

            if (!view.DocumentUrl.IsNullOrEmpty())
            {
                view.Urls.Add(new UrlEditModel()
                {
                    Title = view.DocumentUrl,
                    Url = view.DocumentUrl
                });
            }

            view.IsDraft = idea.IsDraft;
            return view;
        }

        public IEnumerable<SelectListItem> GetAllIdeasSelectList(string ideaId)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {

                return noSqlSession.Find<Idea>(GetIdeaQuery())
                    .SetSortOrder(SortBy.Ascending("Subject"))
                    .SetFields("_id", "Subject", "IsDraft").ToList()
                    .Where(q => q.IsDraft == false && q.Id != ideaId).Select(i => new TextValue()
                    {
                        Text = i.Subject,
                        Value = i.Id
                    }).ToList().ToSelectList();
            }
        }

        public IEnumerable<SelectListItem> GetMyIdeasSelectList(string ideaId = null, List<int> categoryIds = null, string organizationId = null)
        {
            if (categoryIds == null)
            {
                categoryIds = categoryService.GetMyCategoryIds();
            }

            using (var noSqlSession = noSqlSessionFactory())
            {
                List<BsonValue> arr = new List<BsonValue>();
                foreach (var item in categoryIds)
                {
                    arr.Add(BsonValue.Create(item));
                }

                var query = Query.And
                    (GetIdeaQuery(),
                     Query.In("CategoryIds", arr.ToArray()));

                return noSqlSession.Find<Idea>(query)
                    .SetSortOrder(SortBy.Ascending("Subject"))
                    .SetFields("_id", "Subject", "IsDraft", "IsPrivateToOrganization", "OrganizationId").ToList()
                    .Where(q => q.IsDraft == false && q.Id != ideaId && (!q.IsPrivateToOrganization || q.OrganizationId == organizationId))
                    .Select(i => new TextValue()
                    {
                        Text = i.Subject,
                        Value = i.Id
                    }).ToList().ToSelectList();
            }
        }

        private MongoDB.Driver.IMongoQuery GetIdeaQuery()
        {
            List<BsonValue> orgsBson = new List<BsonValue>();
            foreach (var org in CurrentUser.OrganizationIds)
            {
                orgsBson.Add(BsonValue.Create(org));
            }

            var q = Query.And
                    (Query.NotIn("State", new[] { BsonValue.Create(IdeaStates.Closed), BsonValue.Create(IdeaStates.Realized) }),
                     Query.Or(Query.NotExists("IsPrivateToOrganization"), Query.EQ("IsPrivateToOrganization", false), Query.In("OrganizationId", orgsBson.ToArray())));
            return q;
        }

        public List<RelatedIdeaListItem> GetRelatedIdeas(string ideaId)
        {
            using (var votingSession = votingSessionFactory.CreateContext())
            {
                var idea = GetIdea(ideaId);
                var canEdit = CanCurrentUserEdit(idea);
                var relatedIdeas = votingSession.RelatedIdeas.Where(i => i.IdeaObjectId == ideaId || i.RelatedIdeaObjectId == ideaId)
                    .Select(cId => new RelatedIdeaListItem
                {
                    ObjectId = cId.IdeaObjectId == ideaId ? cId.RelatedIdeaObjectId : cId.IdeaObjectId,
                    IsDeletable = canEdit
                }).ToList();

                return GetRelatedIdeas(relatedIdeas);
            }
        }

        public List<RelatedIdeaListItem> GetRelatedIssueIdeas(string issueId)
        {
            using (var votingSession = votingSessionFactory.CreateContext())
            {
                var relatedIdeas = votingSession.IdeaIssues.Where(i => i.IssueObjectId == issueId)
                    .Select(cId => new RelatedIdeaListItem
                    {
                        ObjectId = cId.IdeaObjectId
                    }).ToList();

                return GetRelatedIdeas(relatedIdeas);
            }
        }

        private List<ListItem> GetRelatedIssues(string ideaId)
        {
            using (var votingSession = votingSessionFactory.CreateContext())
            {
                var relatedIssues = votingSession.IdeaIssues.Where(i => i.IdeaObjectId == ideaId).Select(cId => new ListItem
                {
                    ObjectId = cId.IssueObjectId,
                }).ToList();

                var idea = GetIdea(ideaId);

                foreach (var i in relatedIssues)
                {
                    var issue = GetIssue(i.ObjectId);
                    i.Name = issue.Subject;
                    i.IsDeleted = issue.IsPrivateToOrganization && !CurrentUser.IsUserInOrganization(issue.OrganizationId);
                    i.IsDeletable = CanCurrentUserEdit(idea);
                }

                relatedIssues = relatedIssues.Where(r => !r.IsDeleted).ToList();

                return relatedIssues;
            }
        }

        public List<RelatedIdeaListItem> GetRelatedIdeas(List<RelatedIdeaListItem> relatedIdeas)
        {
            foreach (var relatedIdea in relatedIdeas)
            {
                var idea = GetIdea(relatedIdea.ObjectId);
                if (idea != null)
                {
                    relatedIdea.Name = idea.Subject;
                    relatedIdea.State = idea.ActualState;
                    relatedIdea.InitiativeType = idea.InitiativeType;
                    relatedIdea.SupportersCount = idea.SummaryWiki.Versions.Sum(c => c.SupportingUserCount);
                    relatedIdea.IsVisible = !idea.IsPrivateToOrganization ||
                                            CurrentUser.IsUserInOrganization(idea.OrganizationId);
                    relatedIdea.IsDeletable = relatedIdea.IsDeletable || CanCurrentUserEdit(idea);
                }
                else
                {
                    relatedIdea.IsVisible = false;
                }
            }

            return relatedIdeas.Where(r => r.IsVisible).ToList();
        }

        public bool Edit(IdeaCreateEditModel model)
        {
            var idea = GetIdea(model.Id);

            if (!CanCurrentUserEdit(idea))
            {
                throw new UnauthorizedAccessException("Cannot edit");
            }

            var wasDraft = idea.IsDraft;

            if (model.Draft != null)
            {
                model.IsDraft = true;
            }
            if (idea == null)
            {
                throw new Exception("Idea Id cannot be null");
            }

            //idea.InjectFrom<UniversalInjection>(model);

            idea.Aim = model.Aim.Sanitize();
            idea.Subject = model.Subject.GetSafeHtml();

            if (WorkContext.Municipality == null)
            {
                idea.MunicipalityId = model.MunicipalityId;
            }
            if (idea.MunicipalityId == 0)
            {
                idea.MunicipalityId = null;
            }

            if (WorkContext.CategoryIds != null)
            {
                idea.CategoryIds = WorkContext.CategoryIds;
            }
            else
            {
                idea.CategoryIds.Clear();
                idea.CategoryIds = model.CategoryIds;
            }

            model.Urls.BindUrls(idea.Urls);
            idea.DocumentUrl = null;

            idea.SendMail = model.SendMail;
            idea.Visibility = model.Visibility;
            idea.ConfirmedUsersVoting = model.ConfirmedUsersVoting;
            idea.ForbidPublicAlternativeIdeas = !model.AllowPublicAlternativeIdeas;

            SaveRelatedIdeas(idea, model.RelatedIdeas);
            SaveRelatedIssues(idea, model.RelatedIssues);

            var problemIds =
                model.Problems.Where(p => !string.IsNullOrEmpty(p.Id)).Select(p => p.Id).Distinct().ToList();
            foreach (var problemId in problemIds)
            {
                AddProblemIdea(problemId, idea.Id);
            }

            DeleteRemovedProblemIdeas(problemIds, idea.Id);

            UpdateIdea(idea);

            if (!wasDraft)
            {
                SaveAimAsProblem(idea);
                var command = new IdeaCommand
                                  {
                                      ActionType = ActionTypes.IdeaEdited,
                                      UserId = CurrentUser.Id,
                                      ObjectId = idea.Id.ToString(),
                                      IsPrivate = idea.IsPrivateToOrganization,
                                      Link = Url.Action("Details", "Idea", new { id = idea.Id }),
                                      SendMail = idea.SendMail
                                  };

                bus.Send(command);
            }
            else if (!model.IsDraft)
            {
                Publish(idea);
                return true;
            }

            return false;
        }

        public void SaveRelatedIdeas(RelatedIdeaDialogModel model)
        {
            var idea = GetIdea(model.Id);
            SaveRelatedIdeas(idea, model.RelatedIdeas);

            bus.Send(new IdeaCommand
            {
                ActionType = ActionTypes.RelatedIdeaAdded,
                UserId = CurrentUser.Id,
                ObjectId = idea.Id.ToString(),
                IsPrivate = idea.IsPrivateToOrganization,
                Link = Url.Action("Details", "Idea", new { id = idea.Id })
            });
        }

        public void SaveRelatedIdeas(Idea idea, List<RelatedIdeaListItem> relatedIdeas)
        {
            idea.RelatedIdeas = relatedIdeas.Where(ri => !ri.IsDeleted && ri.ObjectId != idea.Id).Select(ri => ri.ObjectId).Distinct().ToList();
            using (var votingSession = votingSessionFactory.CreateContext())
            {
                SaveDbRelatedIdeas(idea, CurrentUser.Id);
            }
        }

        public void SaveRelatedIssues(RelatedIssueDialogModel model)
        {
            var idea = GetIdea(model.Id);
            SaveRelatedIssues(idea, model.RelatedIssues);
            bus.Send(new IdeaCommand
            {
                ActionType = ActionTypes.RelatedIssueAdded,
                UserId = CurrentUser.Id,
                ObjectId = idea.Id.ToString(),
                IsPrivate = idea.IsPrivateToOrganization,
                Link = Url.Action("Details", "Idea", new { id = idea.Id })
            });
        }

        public void SaveRelatedIssues(Idea idea, List<ListItem> relatedIssues)
        {
            using (var votingSession = votingSessionFactory.CreateContext(true))
            {
                string ideaId = idea.Id;
                votingSession.IdeaIssues.Delete(ic => ic.IdeaObjectId == ideaId);
                foreach (var relatedIssue in relatedIssues)
                {
                    var dbIdea = new Data.EF.Voting.IdeaIssue()
                    {
                        IdeaObjectId = ideaId,
                        IssueObjectId = relatedIssue.ObjectId,
                        ChangeState = false
                    };

                    votingSession.IdeaIssues.Add(dbIdea);
                }
            }
        }

        public void SaveAimAsProblem(Data.MongoDB.Idea idea)
        {
            if (idea.SaveAimAsProblem && !string.IsNullOrEmpty(idea.Aim))
            {
                var result = ProblemService.Insert(new ProblemCreateEditModel()
                {
                    MunicipalityId = idea.MunicipalityId,
                    OrganizationId = idea.OrganizationId,
                    Text = idea.Aim,
                    CategoryIds = idea.CategoryIds,
                    IsPrivate = idea.IsPrivateToOrganization
                }, null);

                AddProblemIdea(result.Id, idea.Id);

                idea.Aim = null;

                UpdateIdea(idea);
            }
        }

        private void AddProblemIdea(string problemId, string ideaId)
        {
            using (var votingSession = votingSessionFactory.CreateContext(true))
            {
                if (votingSession.ProblemIdeas.Any(p => p.ProblemId == problemId && p.IdeaId == ideaId))
                {
                    return;
                }

                votingSession.ProblemIdeas.Add(new ProblemIdea()
                {
                    ProblemId = problemId,
                    IdeaId = ideaId,
                    UserObjectId = CurrentUser.Id
                });
            }
        }

        private void DeleteRemovedProblemIdeas(List<string> problemIds, string ideaId)
        {
            using (var votingSession = votingSessionFactory.CreateContext(true))
            {
                var deletedProblems =
                    votingSession.ProblemIdeas.Where(p => !problemIds.Contains(p.ProblemId) && p.IdeaId == ideaId);
                votingSession.ProblemIdeas.RemoveRange(deletedProblems);
            }
        }

        public bool SendNotification(string ideaId)
        {
            if (!UserService.CanSendMail())
            {
                return false;
            }
            var idea = GetIdea(ideaId);
            if (!idea.IsMailSent)
            {
                UserService.SetMailSendDate();
                bus.Send(new SendObjectCreatedNotificationCommand()
                             {
                                 ObjectId = ideaId,
                                 Link = Url.ActionAbsolute("Details", "Idea",
                                                           new { id = idea.Id, subject = idea.Subject.ToSeoUrl() }),
                                 Type = NotificationTypes.IdeaCreated,
                                 UserObjectId = CurrentUser.Id
                             });
                return true;
            }

            return false;
        }

        public IdeaViewModel Publish(string ideaId)
        {
            return Publish(GetIdea(ideaId));
        }

        public IdeaViewModel Publish(Idea idea)
        {
            if (!CanCurrentUserEdit(idea))
            {
                throw new AccessViolationException();
            }

            idea.IsDraft = false;
            UpdateIdea(idea);

            using (var session = votingSessionFactory.CreateContext(true))
            {
                var id = idea.Id.ToString();
                var dbIdea = session.Ideas.Single(i => i.Id == id);
                dbIdea.IsDraft = false;
                dbIdea.ModifiedOn = DateTime.Now;
            }

            SaveAimAsProblem(idea);

            var command = new IdeaCommand
                              {
                                  ActionType = ActionTypes.IdeaCreated,
                                  UserId = CurrentUser.Id,
                                  ObjectId = idea.Id.ToString(),
                                  Link =
                                      Url.ActionAbsolute("Details", "Idea",
                                                         new { id = idea.Id, subject = idea.Subject.ToSeoUrl() }),
                                  IsPrivate = idea.IsPrivateToOrganization,
                                  SendMail = idea.SendMail
                              };

            bus.Send(command);
            return GetViewModelFromIdea(idea, null);
        }

        public IdeaCreateEditModel Create(string relatedIdeaId = null, string problemId = null, string organizationId = null, string issueId = null)
        {
            var model = new IdeaCreateEditModel();

            model = FillCreateEditModel(model);

            if (!string.IsNullOrEmpty(problemId))
            {
                var problem = ProblemService.GetProblem(problemId);
                model.Problems.Add(ProblemService.GetProblemIndexItem(problem, null, false));
                if (!model.OrganizationId.IsNullOrEmpty())
                {
                    model.OrganizationId = problem.OrganizationId;
                }

                model.MunicipalityId = problem.MunicipalityId;
                if (!string.IsNullOrEmpty(model.OrganizationId))
                {
                    model.Visibility = ObjectVisibility.Private;
                }
                model.CategoryIds = problem.CategoryIds;

                using (var votingSession = votingSessionFactory.CreateContext(true))
                {
                    var otherProblemIdeas =
                        votingSession.ProblemIdeas.Where(pi => pi.ProblemId == problemId &&
                            (pi.Problem.IsPrivateToOrganization == false || CurrentUser.OrganizationIds.Contains(pi.Problem.OrganizationId)))
                            .Select(pi => new { pi.IdeaId, pi.Idea.Subject })
                            .ToList();
                    foreach (var problemIdea in otherProblemIdeas)
                    {
                        model.RelatedIdeas.Add(new RelatedIdeaListItem()
                                                   {
                                                       Name = problemIdea.Subject,
                                                       ObjectId = problemIdea.IdeaId,
                                                       IsDeletable = true
                                                   });
                    }
                }
            }

            if (!string.IsNullOrEmpty(relatedIdeaId))
            {
                var relatedIdea = GetIdea(relatedIdeaId);
                model.CategoryIds = relatedIdea.CategoryIds;
                model.MunicipalityId = relatedIdea.MunicipalityId;
                model.Aim = relatedIdea.Aim;
                model.Problems = GetIdeaProblems(relatedIdeaId);
                var relatedIdeas = GetRelatedIdeas(relatedIdeaId);
                relatedIdeas.Add(new RelatedIdeaListItem()
                {
                    Name = relatedIdea.Subject,
                    ObjectId = relatedIdeaId
                });

                model.RelatedIdeas = relatedIdeas;

                if (CurrentUser.IsUserInOrganization(relatedIdea.OrganizationId))
                {
                    model.OrganizationId = relatedIdea.OrganizationId;
                    model.Visibility = relatedIdea.Visibility;
                }
            }

            if (!string.IsNullOrEmpty(issueId))
            {
                var relatedIssue = GetIssue(issueId);
                model.CategoryIds = relatedIssue.CategoryIds;
                model.MunicipalityId = relatedIssue.MunicipalityId;
                model.Problems = GetIssueProblems(issueId);
                model.RelatedIdeas = GetRelatedIssueIdeas(issueId);
                model.RelatedIssues.Add(new ListItem()
                    {
                        ObjectId = issueId,
                        Name = relatedIssue.Subject
                    });

                if (CurrentUser.IsUserInOrganization(relatedIssue.OrganizationId))
                {
                    model.OrganizationId = relatedIssue.OrganizationId;
                    model.Visibility = relatedIssue.Visibility;
                }
            }

            if (!string.IsNullOrEmpty(organizationId))
            {
                model.Visibility = ObjectVisibility.Private;
                model.OrganizationId = organizationId;
                model.OrganizationName = OrganizationService.GetOrganizationName(organizationId);
            }

            return model;
        }

        public IdeaCreateEditModel FillCreateEditModel(IdeaCreateEditModel model)
        {
            model.Categories = categoryService.GetCategories().ToSelectList();
            model.Municipalities = addressService.GetMunicipalities(1);
            model.Organizations = OrganizationService.GetUserOrganizations();
            model.OrganizationName = !string.IsNullOrEmpty(model.OrganizationId)
                ? model.Organizations.Where(o => o.Value == model.OrganizationId).Select(o => o.Text).SingleOrDefault()
                : string.Empty;
            model.OrganizationId = model.OrganizationId ?? string.Empty;
            model.Ideas = GetMyIdeasSelectList(model.Id);
            model.IsMailSendable = UserService.IsMailSendable();
            if (model.Urls == null)
            {
                model.Urls = new List<UrlEditModel>();
            }
            return model;
        }

        public string Insert(IdeaCreateEditModel model)
        {
            if (WorkContext.CategoryIds != null)
            {
                model.CategoryIds =  WorkContext.CategoryIds;
            }

            var idea = new Idea
                           {
                               CategoryIds = model.CategoryIds,
                               RegistrationDate = DateTime.Now,
                               ModificationDate = DateTime.Now,
                               UserObjectId = CurrentUser.Id,
                               UserFullName = CurrentUser.FullName,
                               OrganizationId = !string.IsNullOrEmpty(model.OrganizationId) ? model.OrganizationId : null,
                               Visibility = model.Visibility,
                               Aim = model.Aim.Sanitize(),
                               Subject = model.Subject,
                               ConfirmedUsersVoting = model.ConfirmedUsersVoting,
                               ForbidPublicAlternativeIdeas = !model.AllowPublicAlternativeIdeas
                           };

            if (WorkContext.Municipality != null)
            {
                idea.MunicipalityId = WorkContext.Municipality.Id;
            }
            else
            {
                idea.MunicipalityId = model.MunicipalityId;
            }

            if (idea.MunicipalityId == 0)
            {
                idea.MunicipalityId = null;
            }

            var version = new WikiTextVersionWithHistory
                              {
                                  CreatedOn = DateTime.Now,
                                  CreatorFullName = CurrentUser.FullName,
                                  CreatorObjectId = CurrentUser.Id,
                                  OrganizationId = model.OrganizationId,
                                  OrganizationName = OrganizationService.GetOrganizationName(model.OrganizationId),
                                  Text = model.Summary.RemoveNewLines().Sanitize(),
                                  Subject = model.VersionSubject.GetSafeHtml(),
                                  Number = 1
                              };

            model.Urls.BindUrls(idea.Urls);
            model.Attachments.BindUrls(version.Attachments);

            idea.SummaryWiki.Versions.Add(version);
            idea.RelatedIdeas = model.RelatedIdeas.Select(ri => ri.ObjectId).Distinct().ToList();


            if (model.Draft != null)
            {
                idea.IsDraft = true;
            }

            idea.SendMail = model.SendMail;
            idea.ShortLink = model.ShortLink ?? GetShortLink(idea);

            using (var noSqlSession = noSqlSessionFactory())
            {
                noSqlSession.Add(idea);
            }

            if (!idea.IsDraft)
            {
                SaveHistory(version);
            }

            UpdateDbIdea(idea);
            SaveRelatedIdeas(idea, model.RelatedIdeas);
            SaveRelatedIssues(idea, model.RelatedIssues);

            var problemIds =
                model.Problems.Where(p => !string.IsNullOrEmpty(p.Id)).Select(p => p.Id).Distinct().ToList();
            foreach (var problemId in problemIds)
            {
                AddProblemIdea(problemId, idea.Id);
            }

            ActionService.Subscribe(idea.Id, CurrentUser.DbId.Value, EntryTypes.Idea);

            if (model.Save != null)
            {
                SaveAimAsProblem(idea);
                var command = new IdeaCommand
                                  {
                                      ActionType = ActionTypes.IdeaCreated,
                                      UserId = CurrentUser.Id,
                                      ObjectId = idea.Id.ToString(),
                                      Link =
                                          Url.ActionAbsolute("Details", "Idea",
                                                             new { id = idea.Id, subject = idea.Subject.ToSeoUrl() }),
                                      IsPrivate = idea.IsPrivateToOrganization,
                                      SendMail = model.SendMail
                                  };

                bus.Send(command);
            }

            return idea.Id;
        }

        private string GetShortLink(Idea idea)
        {
            return shortLinkService.GetShortLink(idea.ShortLink ?? idea.Subject,
                                                 GetDetailsUrl(idea));
        }

        private string GetDetailsUrl(Idea idea)
        {
            return Url.Action("Details", "Idea", new { id = idea.Id, subject = idea.Subject.ToSeoUrl() });
        }

        public void UpdateShortLinks()
        {
            foreach (var idea in GetAllIdeas())
            {
                idea.ShortLink = GetShortLink(idea);
                UpdateIdea(idea);
            }
        }

        public IdeaViewModel GetIdeaView(MongoObjectId id, MongoObjectId versionId)
        {
            var view = GetIdeaViewInternal(id, versionId);

            SetPageViewed(view);

            return view;
        }

        public IEnumerable<WikiVersionModel> GetVersions(MongoObjectId ideaId)
        {
            object versions;
            if (cache.GetItem(GetCacheKey(ideaId), out versions))
            {
                return versions as IEnumerable<WikiVersionModel>;
            }

            return GetIdeaViewInternal(ideaId, null).Versions;
        }

        private bool IsVersionEditable(Idea idea, WikiTextVersionWithHistory version)
        {
            return (version.CreatorObjectId == CurrentUser.Id || CurrentUser.Role == UserRoles.Admin ||
                    CurrentUser.ExpertCategoryIds.Intersect(idea.CategoryIds).Any());
        }

        public IdeaViewModel AddNewVersion(IdeaViewModel model)
        {
            var idea = GetIdea(model.Id);
            WikiTextVersionWithHistory version = idea.SummaryWiki.Versions.Where(v => v.Id == model.CurrentVersion.Id).Single();

            var createNewVersion = model.CurrentVersion.CreateNewVersion || !IsVersionEditable(idea, version);

            if (createNewVersion)
            {
                if (idea.ForbidPublicAlternativeIdeas && !CanCurrentUserEdit(idea))
                {
                    throw new UnauthorizedAccessException();
                }

                if (idea.IsPrivateToOrganization)
                {
                    model.OrganizationId = idea.OrganizationId;
                }

                version = new WikiTextVersionWithHistory()
                    {
                        CreatedOn = DateTime.Now,
                        CreatorFullName = CurrentUser.FullName,
                        CreatorObjectId = CurrentUser.Id,
                        OrganizationId = model.OrganizationId,
                        OrganizationName = OrganizationService.GetOrganizationName(model.OrganizationId),
                        Number = idea.SummaryWiki.Versions.Max(v => v.Number) + 1
                    };

                idea.SummaryWiki.Versions.Add(version);
            }
            else if (!version.History.Any() && !idea.IsDraft)
            {
                var versionForHistory = new WikiTextVersionWithHistory();
                versionForHistory.InjectFrom<UniversalInjection>(version);
                versionForHistory.History = null;
                versionForHistory.Id = BsonObjectId.GenerateNewId();
                version.History.Add(versionForHistory);
            }


            version.Text = model.CurrentVersion.Text.RemoveNewLines().Sanitize();
            version.Subject = model.CurrentVersion.Subject;
            model.Attachments.BindUrls(version.Attachments);

            cache.InvalidateItem(GetCacheKey(model.Id));

            if (!idea.IsDraft)
            {
                SaveHistory(version);
            }

            UpdateIdea(idea);

            if (!idea.IsDraft)
            {
                if (createNewVersion)
                {
                    bus.Send(new IdeaCommand
                                 {
                                     ActionType = ActionTypes.IdeaVersionAdded,
                                     UserId = CurrentUser.Id,
                                     ObjectId = model.Id,
                                     Text = model.CurrentVersion.Subject.RemoveNewLines().Sanitize(),
                                     VersionId = version.Id.ToString(),
                                     IsPrivate = idea.IsPrivateToOrganization,
                                     Link = Url.Action("Details", "Idea", new { id = idea.Id, versionId = version.Id.ToString() })
                                 });
                }
                else
                {
                    bus.Send(new IdeaCommand
                                 {
                                     ActionType = ActionTypes.IdeaVersionEdited,
                                     UserId = CurrentUser.Id,
                                     ObjectId = model.Id,
                                     Text = model.CurrentVersion.Subject.RemoveNewLines().Sanitize(),
                                     VersionId = version.Id.ToString(),
                                     IsPrivate = idea.IsPrivateToOrganization,
                                     Link = Url.Action("Details", "Idea", new { id = idea.Id, versionId = version.Id.ToString() })
                                 });
                }
            }

            return GetViewModelFromIdea(idea, version.Id);
        }

        private void SaveHistory(WikiTextVersionWithHistory version)
        {
            var versionForHistory = new WikiTextVersionWithHistory();
            versionForHistory.InjectFrom<UniversalInjection>(version);
            versionForHistory.History = null;
            versionForHistory.Id = BsonObjectId.GenerateNewId();
            versionForHistory.CreatorObjectId = CurrentUser.Id;
            versionForHistory.CreatorFullName = CurrentUser.FullName;
            versionForHistory.CreatedOn = DateTime.Now;
            version.History.Add(versionForHistory);
        }

        protected override ICommentable GetEntity(MongoObjectId id)
        {
            return GetIdea(id);
        }

        protected override ActionTypes GetAddNewCommentActionType()
        {
            return ActionTypes.IdeaCommented;
        }

        protected override ActionTypes GetLikeCommentActionType()
        {
            return ActionTypes.IdeaCommentLiked;
        }

        protected override void SendCommentCommand(ICommentable entity, ActionTypes actionType, CommentView comment)
        {
            var idea = entity as Data.MongoDB.Idea;
            if (!idea.IsDraft)
            {
                var relatedUserId = comment.AuthorObjectId;
                var versionId = comment.VersionId;
                if (!string.IsNullOrEmpty(comment.ParentId))
                {
                    var parentComment = idea.Comments.Single(c => c.Id == comment.ParentId);
                    if (actionType == ActionTypes.CommentCommented)
                    {
                        relatedUserId = parentComment.UserObjectId;
                    }

                    if (string.IsNullOrEmpty(versionId))
                    {
                        versionId = parentComment.RelatedVersionId;
                    }
                }

                bus.Send(new IdeaCommand
                             {
                                 ActionType = actionType,
                                 ObjectId = comment.EntryId,
                                 Text = comment.CommentText,
                                 RelatedUserId = relatedUserId,
                                 UserId = CurrentUser.Id,
                                 CommentId = !string.IsNullOrEmpty(comment.ParentId) ? comment.ParentId : comment.Id,
                                 CommentCommentId = !string.IsNullOrEmpty(comment.ParentId) ? comment.Id : null,
                                 Subject = versionId != null ? idea.SummaryWiki.Versions.Single(v => v.Id == versionId).Subject : GetCurrentVersion(idea).Subject,
                                 Link = Url.Action("Details", "Idea", new { id = idea.Id, versionId }),
                                 IsPrivate = idea.IsPrivateToOrganization
                             });
            }
        }

        public IdeaViewModel GetVersion(MongoObjectId id, MongoObjectId versionId)
        {
            if (CurrentUser.IsAuthenticated)
            {
                SetVersionViewed(versionId);
            }

            var idea = GetIdea(id);
            return GetViewModelFromIdea(idea, versionId);
        }

        public IdeaViewModel Vote(MongoObjectId id, MongoObjectId versionId)
        {
            var idea = GetIdea(id);
            if (idea.IsClosed)
            {
                return null;
            }

            if (idea.ActualState == IdeaStates.Resolved)
            {
                if (CustomAppSettings.IsViispEnabled && idea.AdditionalInfoRequiredForVoting)
                {
                    if (!CurrentUser.IsUnique || !CurrentUser.IsConfirmedThisSession)
                    {
                        throw new UserNotUniqueViispException();
                    }

                    if (!CurrentUser.IsViispConfirmed)
                    {
                        throw new UserNotUniqueViispException();
                    }
                }
                else
                {
                    if (!CurrentUser.IsUnique)
                    {
                        throw new UserNotUniqueException();
                    }

                    if (!CurrentUser.IsConfirmedThisSession)
                    {
                        if (CurrentUser.AuthenticationSource == "vb2" || CurrentUser.AuthenticationSource == "hanza")
                        {
                            throw new PersonCodeNotConfirmedException();
                        }
                        else
                        {
                            throw new UserNotUniqueException();
                        }
                    }
                }

                if (!CurrentUser.CanSign)
                {
                    throw new UserCannotSignException();
                }
            }
            else
            {
                if (!CurrentUser.IsAuthenticated)
                {
                    throw new UnauthorizedAccessException();
                }

                if (idea.ConfirmedUsersVoting)
                {
                    if (!CurrentUser.IsUnique)
                    {
                        throw new UserNotUniqueException();
                    }

                    if (!CurrentUser.CanSign)
                    {
                        throw new UserCannotSignException();
                    }

                    if (!CurrentUser.IsConfirmedThisSession)
                    {
                        throw new PersonCodeNotConfirmedException();
                    }
                }
            }

            var notexist = false;
            WikiTextVersion version = null;

            if (idea.ActualState == IdeaStates.Resolved && CurrentUser.IsUnique)
            {
                using (var votingSession = votingSessionFactory.CreateContext(true))
                {
                    var ideaId = id.ToString();
                    if (CurrentUser.IsAuthenticated)
                    {
                        var dbId = CurrentUser.DbId.ToString();
                        var oldVote = votingSession.IdeaVotes.SingleOrDefault(i => i.IdeaId == ideaId && i.PersonCode == dbId && i.UserObjectId == CurrentUser.Id);
                        if (oldVote != null)
                        {
                            votingSession.IdeaVotes.Remove(oldVote);
                        }
                    }

                    var ideaVote = votingSession.IdeaVotes.SingleOrDefault(i => i.IdeaId == ideaId && i.PersonCode == CurrentUser.PersonCode);

                    if (ideaVote == null)
                    {
                        CurrentUser.AdditionalInfo.DocumentNoRequired = idea.DocumentNoRequiredForVoting;

                        if (idea.AdditionalInfoRequiredForVoting && CurrentUser.AdditionalInfo.AdditionalInfoRequired)
                        {
                            var info = UserService.GetAdditionalVotingInfo(CurrentUser.PersonCode);
                            //CurrentUser.AdditionalInfo has been replaced, reset it
                            info.DocumentNoRequired = idea.DocumentNoRequiredForVoting;
                            info.OfficialUrl = idea.OfficialUrl;
                            throw new AdditionalUniqueInfoRequiredException(info);
                        }

                        notexist = true;
                        ideaVote = new IdeaVote()
                        {
                            IdeaId = ideaId,
                            PersonCode = CurrentUser.PersonCode
                        };

                        votingSession.IdeaVotes.Add(ideaVote);

                        ideaVote.Date = DateTime.Now;
                        ideaVote.FirstName = CurrentUser.FirstName;
                        ideaVote.LastName = CurrentUser.LastName;
                        ideaVote.AddressLine = CurrentUser.AdditionalInfo.Address;
                        ideaVote.DocumentNo = CurrentUser.AdditionalInfo.DocumentNo;
                        ideaVote.CityId = CurrentUser.AdditionalInfo.CityId;
                        ideaVote.Source = CurrentUser.AuthenticationSource == "vb2" ? "seb" : CurrentUser.AuthenticationSource == "hanza" ? "swed" : CurrentUser.AuthenticationSource;
                        ideaVote.IsPublic = CurrentUser.VotesArePublic;
                        ideaVote.UserObjectId = CurrentUser.Id ?? UserService.GetUserObjectIdByPersonCode(CurrentUser.PersonCode);
                        ideaVote.Sign(idea.Resolution);

                        try
                        {
                            votingSession.SaveChanges();
                            if (ideaId == CustomAppSettings.MainIdeaId)
                            {
                                CurrentUser.IsMainIdeaVoted = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.Information(string.Join(", ", ideaVote.Signature.Length, ideaVote.Date,
                                                           ideaVote.FirstName, ideaVote.LastName, ideaVote.AddressLine,
                                                           ideaVote.DocumentNo, ideaVote.CityId, ideaVote.Source,
                                                           ideaVote.IsPublic, ideaVote.UserObjectId, ideaVote.IdeaId,
                                                           ideaVote.PersonCode), ex);
                            throw ex;
                        }
                    }
                }
            }
            else if (CurrentUser.IsAuthenticated)
            {
                if (versionId != null)
                {
                    version = idea.SummaryWiki.Versions.Where(v => v.Id.Equals(versionId)).SingleOrDefault();
                }
                else
                {
                    version = GetCurrentVersion(idea);
                }

                var oldVersion = (from v in idea.SummaryWiki.Versions
                                  where v.SupportingUsers.Select(u => u.Id).Contains(CurrentUser.Id)
                                  select v).SingleOrDefault();

                if (oldVersion != null)
                {
                    oldVersion.SupportingUsers.Remove(oldVersion.SupportingUsers.SingleOrDefault(u => u.Id == CurrentUser.Id));
                    oldVersion.SupportingUserCount = oldVersion.SupportingUsers.Count;
                }

                if (version == null)
                {
                    throw new ObjectNotFoundException("Idea version not found");
                }

                if (!version.SupportingUsers.Any(u => u.Id == CurrentUser.Id))
                {
                    version.SupportingUsers.Add(new SupportingUser()
                        {
                            FullName = CurrentUser.FullName,
                            Id = CurrentUser.Id,
                            IsPublic = CurrentUser.VotesArePublic,
                            Date = DateTime.Now,
                            PersonCode = CurrentUser.PersonCode
                        });

                    version.SupportingUserCount = version.SupportingUsers.Count;
                }

                cache.InvalidateItem(GetCacheKey(id));
            }

            UpdateIdea(idea, false);

            if (CurrentUser.IsAuthenticated)
            {
                ActionService.Subscribe(id, CurrentUser.DbId.Value, EntryTypes.Idea);
                if (version != null)
                {
                    var actionVersionId = idea.ActualState != IdeaStates.Resolved ? versionId : null;
                    bus.Send(new IdeaCommand()
                        {
                            ActionType = ActionTypes.IdeaVersionLiked,
                            UserId = CurrentUser.Id,
                            ObjectId = idea.Id.ToString(),
                            Text = string.Format("{0}. {1}", version.Number, version.Subject),
                            VersionId = version.Id.ToString(),
                            IsPrivate = idea.IsPrivateToOrganization || !CurrentUser.VotesArePublic,
                            Link = Url.Action("Details", "Idea", new { id = idea.Id, actionVersionId })
                        });
                }
            }


            CurrentUser.AdditionalInfo = new AdditionalUniqueInfoModel();

            var result = GetViewModelFromIdea(idea, versionId, true);
            result.WasLiked = !notexist;

            return result;
        }

        public IdeaViewModel CancelVote(MongoObjectId id, MongoObjectId versionId)
        {
            var idea = GetIdea(id);
            if (idea.IsClosed)
            {
                return null;
            }
            if (CurrentUser.IsAuthenticated)
            {
                WikiTextVersion version;
                if (versionId != null)
                {
                    version = idea.SummaryWiki.Versions.Where(v => v.Id.Equals(versionId)).SingleOrDefault();
                }
                else
                {
                    version = GetCurrentVersion(idea);
                }

                if (version == null)
                {
                    throw new ObjectNotFoundException("Idea version not found");
                }

                version.SupportingUsers.Remove(version.SupportingUsers.SingleOrDefault(u => u.Id == CurrentUser.Id));
                version.SupportingUserCount = version.SupportingUsers.Count;

                UpdateIdea(idea, false);

                cache.InvalidateItem(GetCacheKey(id));

                bus.Send(new VoteCancelledCommand
                {
                    UserId = CurrentUser.Id,
                    ObjectId = idea.Id.ToString(),
                });
            }
            if (CurrentUser.IsUnique && idea.ActualState == IdeaStates.Resolved)
            {
                using (var votingSession = votingSessionFactory.CreateContext(true))
                {
                    var ideaId = id.ToString();
                    votingSession.IdeaVotes.Delete(i => i.IdeaId == ideaId && i.PersonCode == CurrentUser.PersonCode);
                }

                //MembershipSession.Reset();
            }

            return GetViewModelFromIdea(idea, versionId, true);
        }

        public string GetMyCategoryNames()
        {
            return categoryService.GetMyCategoryNames();
        }

        public SimpleListContainerModel GetCreatedIdeas(string userObjectId, int pageNumber)
        {
            using (var session = votingSessionFactory.CreateContext())
            {
                var result = (from a in session.IdeaVersions
                              where a.UserObjectId == userObjectId && !a.Idea.IsImpersonal && (!a.Idea.IsPrivate || CurrentUser.OrganizationIds.Contains(a.Idea.OrganizationId))
                              select new SimpleListModel
                  {
                      Id = a.IdeaId,
                      Subject = a.Idea.Subject,
                      Date = a.Idea.CreatedOn
                  }).Distinct().OrderByDescending(i => i.Date).GetExpandablePage(pageNumber, CustomAppSettings.PageSizeList).ToList();

                result.ForEach(r => r.Type = EntryTypes.Idea);
                var simpleList = new SimpleListContainerModel();
                simpleList.List = new ExpandableList<SimpleListModel>(result, CustomAppSettings.PageSizeList);
                return simpleList;
            }
        }

        public SimpleListContainerModel GetInvolvedIdeas(string userObjectId, int pageNumber)
        {
            using (var actionSession = actionSessionFactory.CreateContext())
            {
                var result = (from a in actionSession.Actions
                              where a.UserObjectId == userObjectId &&
                              (a.ActionTypeId == (int)ActionTypes.IdeaCommented ||
                              a.ActionTypeId == (int)ActionTypes.IdeaVersionLiked)
                              && (!a.IsPrivate || CurrentUser.OrganizationIds.Contains(a.OrganizationId)) && !a.IsDeleted
                              group a by new { Id = a.ObjectId, Subject = a.Subject } into g
                              select new SimpleListModel
                  {
                      Id = g.Key.Id,
                      Subject = g.Key.Subject,
                      Date = g.Max(a => a.Date)
                  }).Distinct().OrderByDescending(i => i.Date).GetExpandablePage(pageNumber, CustomAppSettings.PageSizeList).ToList();

                result.ForEach(r => r.Type = EntryTypes.Idea);
                var simpleList = new SimpleListContainerModel();
                simpleList.List = new ExpandableList<SimpleListModel>(result, CustomAppSettings.PageSizeList);
                return simpleList;
            }
        }

        private string GetCacheKey(MongoObjectId ideaObjectId)
        {
            if (CurrentUser.IsAuthenticated)
            {
                return string.Format("Idea_{0}_User_{1}_Version_Cache", ideaObjectId, CurrentUser.DbId);
            }

            return string.Format("Idea_{0}_Version_Cache", ideaObjectId);
        }

        private IdeaViewModel GetViewModelFromIdea(Idea idea, MongoObjectId versionId, bool afterVote = false)
        {
            var view = new IdeaViewModel();
            //view.InjectFrom<UniversalInjection>(idea);
            view.Id = idea.Id;
            view.Subject = idea.Subject;
            view.DocumentUrl = idea.DocumentUrl;
            view.FinalVersionId = idea.FinalVersionId;
            view.InitiativeType = idea.InitiativeType;
            view.IsDraft = idea.IsDraft;
            view.OrganizationId = idea.OrganizationId;
            view.ProjectId = idea.ProjectId;
            view.Resolution = idea.Resolution;
            view.StateDescription = idea.StateDescription;
            view.UserObjectId = idea.UserObjectId;
            view.Categories = idea.CategoryIds.Select(cId => categoryService.GetCategoryName(cId)).ToList();
            view.CategoryIds = idea.CategoryIds;
            view.TotalSupporters = idea.SummaryWiki.Versions.Sum(v => v.SupportingUserCount);
            view.TotalConfirmedSupporters = idea.SummaryWiki.Versions.Sum(v => v.SupportingUsers.Count(u => !string.IsNullOrEmpty(u.PersonCode)));
            view.TotalUnconfirmedSupporters = idea.SummaryWiki.Versions.Sum(v => v.SupportingUsers.Count(u => string.IsNullOrEmpty(u.PersonCode)));
            view.RequiredVotes = idea.RequiredVotes;
            view.Municipality = addressService.GetMunicipality(idea.MunicipalityId);
            view.Aim = idea.Aim;
            view.Deadline = idea.Deadline;
            view.IsImpersonal = idea.IsImpersonal;
            view.ShortLink = idea.ShortLink ?? string.Empty;
            view.State = idea.ActualState;
            view.Visibility = idea.Visibility;
            view.IsClosed = idea.IsClosed;
            view.OfficialUrl = idea.OfficialUrl;
            view.AllowPublicAlternativeIdeas = !idea.ForbidPublicAlternativeIdeas;
            view.PromoteToFrontPage = idea.PromoteToFrontPage;

            if (idea.Deadline.HasValue)
            {
                view.DeadlineTime = idea.Deadline.Value.TimeOfDay;
            }
            IList<Data.EF.Voting.IdeaVersionView> viewedVersions = null;
            if (CurrentUser.IsAuthenticated && !afterVote)
            {
                using (var votingSession = votingSessionFactory.CreateContext())
                {
                    string ideaId = idea.Id.ToString();
                    viewedVersions = (from v in votingSession.IdeaVersionViews
                                      where v.IdeaVersion.IdeaId == ideaId && v.UserDbId == CurrentUser.DbId
                                      select v).Distinct().ToList();
                }
            }
            view.Versions = GetVersionOrder(idea.SummaryWiki.Versions).Select(v => new WikiVersionModel()
                                                                      {
                                                                          Id = v.Id,
                                                                          IsLikedByCurrentUser = CurrentUser.IsAuthenticated &&
                                                                              v.SupportingUsers.Any(u => u.Id == CurrentUser.Id),
                                                                          IsViewedByCurrentUser = GetIsViewedByCurrentUser(v.CreatorObjectId, v.Id, viewedVersions),
                                                                          IsCreatedByCurrentUser = CurrentUser.IsAuthenticated && CurrentUser.Id == v.CreatorObjectId,
                                                                          IsEditable = CurrentUser.IsAuthenticated && (CurrentUser.Id == v.CreatorObjectId || CurrentUser.Role == UserRoles.Admin || CurrentUser.ExpertCategoryIds.Intersect(idea.CategoryIds).Any()),
                                                                          SupportingUserCount = v.SupportingUserCount,
                                                                          SupportingConfirmedUserCount = v.SupportingUsers.Count(u => !string.IsNullOrEmpty(u.PersonCode)),
                                                                          SupportingUnconfirmedUserCount = v.SupportingUsers.Count(u => string.IsNullOrEmpty(u.PersonCode)),
                                                                          SupportPercentage = GetSupportPercentageString(v.SupportingUserCount, view.TotalSupporters),
                                                                          CreatorFullName = v.CreatorFullName,
                                                                          CreatorObjectId = v.CreatorObjectId,
                                                                          OrganizationId = v.OrganizationId,
                                                                          OrganizationName = v.OrganizationName,
                                                                          Text = v.Text,
                                                                          Subject = v.Subject,
                                                                          Number = v.Number,
                                                                          CreatedOn = v.CreatedOn,
                                                                          History = v.History.Select(h => new WikiVersionModel()
                                                                              {
                                                                                  Id = h.Id,
                                                                                  Text = h.Text,
                                                                                  Subject = h.Subject,
                                                                                  CreatorObjectId = h.CreatorObjectId,
                                                                                  CreatorFullName = h.CreatorFullName,
                                                                                  CreatedOn = h.CreatedOn
                                                                              }).ToList(),
                                                                          Attachments = v.Attachments.Select(a => new UrlViewModel()
                                                                                                                      {
                                                                                                                          Url = a.Url,
                                                                                                                          IconUrl = a.IconUrl,
                                                                                                                          Title = a.Title
                                                                                                                      }).ToList()
                                                                      }).ToList();

            view.CurrentVersion = (versionId != null ? view.Versions.SingleOrDefault(v => v.Id == versionId) : GetCurrentVersion(view)) ??
                                  GetCurrentVersion(view);

            view.CurrentVersion.IsViewedByCurrentUser = true;

            view.CurrentVersion.Organizations = OrganizationService.GetUserOrganizations();

            view.IsEditable = CanCurrentUserEdit(idea);
            view.IsContributable = CurrentUser.IsAuthenticated &&
                                   (CurrentUser.Role == UserRoles.Admin ||
                                    CurrentUser.Organizations.Where(o => o.OrganizationId == idea.OrganizationId)
                                               .Select(o => o.Permission)
                                               .SingleOrDefault() != UserRoles.Basic);

            view.IsCurrentUserInvolved = false;
            if (!afterVote)
            {
                if (idea.ProjectId != null)
                {
                    List<ProjectMember> projectMembers = null;
                    using (var noSqlSession = noSqlSessionFactory())
                    {
                        projectMembers =
                            noSqlSession.GetAll<Project>()
                                        .Where(p => p.Id == idea.ProjectId)
                                        .Select(p => p.ProjectMembers)
                                        .Single();
                    }

                    if (CurrentUser.IsAuthenticated)
                    {
                        view.IsCurrentUserInvolved = projectMembers.Select(m => m.UserObjectId).Contains(CurrentUser.Id);
                    }
                }

                view.IsJoinable = view.State == IdeaStates.New && CurrentUser.IsAuthenticated;
                view.IsMailSendable = IsMailSendable(idea, view.IsEditable);

                view.Comments = new CommentsModel()
                    {
                        Comments = commentService.GetCommentsMostSupported(idea, 0),
                        EntryId = view.Id,
                        Type = EntryTypes.Idea
                    };

                foreach (ForAgainst forAgainst in Enum.GetValues(typeof(ForAgainst)))
                {
                    view.Comments.CommentCounts.Add(forAgainst, commentService.GetCommentsCount(idea, forAgainst));
                }

                view.RelatedIssues = GetRelatedIssues(idea.Id);

                view.RelatedIdeas = GetRelatedIdeas(idea.Id);

                view.Problems =
                GetIdeaProblems(idea.Id).Select(p => new SimpleListModel() { Subject = p.Text, Id = p.Id }).ToList();

                view.Urls = idea.Urls.Select(w => new UrlViewModel
                {
                    Title = w.Title,
                    Url = w.Url
                }).ToList();

                if (!view.DocumentUrl.IsNullOrEmpty())
                {
                    view.Urls.Add(new UrlViewModel()
                    {
                        Title = view.DocumentUrl,
                        Url = view.DocumentUrl
                    });
                }

                var list = view.Versions.Select(v => new SelectListItem()
                {
                    Text = v.Subject,
                    Value = v.Id
                }).ToList();
                list.Insert(0, new SelectListItem());
                view.VersionSelectList = list;
            }

            if (idea.RequiredVotes.HasValue)
            {
                view.Progress = new ProgressViewModel()
                                    {
                                        Id = view.Id,
                                        NumberSupporters = GetUniqueSupportersCount(idea.Id),
                                        RequiredVotes = idea.RequiredVotes.Value
                                    };

                view.Progress.SupportPercentage = GetSupportPercentage(view.Progress.NumberSupporters,
                                                                       view.RequiredVotes);
            }

            if (idea.ActualState == IdeaStates.Resolved)
            {
                view.IsLikedByUniqueCurrentUser = IsSupportedByUniqueCurrentUser(view.Id);
            }

            if (CurrentUser.IsAuthenticated)
            {
                view.Subscribe = ActionService.IsSubscribed(idea.Id, CurrentUser.DbId.Value, EntryTypes.Idea);
            }

            return view;
        }

        public bool CanCurrentUserEdit(string ideaId)
        {
            return CanCurrentUserEdit(GetIdea(ideaId));
        }

        private bool CanCurrentUserEdit(Idea idea)
        {
            return CurrentUser.IsAuthenticated &&
                   (CurrentUser.Role == UserRoles.Admin || (!string.IsNullOrEmpty(idea.ResolvedByPersonCode) && idea.ResolvedByPersonCode == CurrentUser.PersonCode && idea.VotesCount == 0) ||
                   CurrentUser.Organizations.Where(o => o.OrganizationId == idea.OrganizationId).Select(o => o.Permission).SingleOrDefault() != UserRoles.Basic ||
                    ((idea.SummaryWiki.Versions.Any(v => v.CreatorObjectId == CurrentUser.Id) ||
                    UserService.IsUserExpert(idea.CategoryIds))));
        }

        private bool IsMailSendable(Idea idea, bool isEditable)
        {
            return !idea.IsMailSent && isEditable && UserService.IsMailSendable() && !idea.IsClosed;
        }

        private int GetSupportPercentage(int supportersCount, int? requiredVotes)
        {
            return !requiredVotes.HasValue || requiredVotes == 0 || supportersCount > requiredVotes ? 100 : (int)(((float)supportersCount / requiredVotes) * 100);
        }

        private int GetUniqueSupportersCount(string id)
        {
            using (var session = votingSessionFactory.CreateContext())
            {
                var cnt = session.IdeaVotes.Where(i => i.IdeaId == id).Count();
                if (cnt == 0)
                {
                    var idea = GetIdea(id);
                    return idea.VotesCount;
                }

                return cnt;
            }
        }

        public List<ProblemIndexItemModel> GetIdeaProblems(string ideaId)
        {
            using (var session = votingSessionFactory.CreateContext())
            {
                var query = from pi in session.ProblemIdeas
                            where
                                pi.IdeaId == ideaId &&
                                (pi.Problem.IsPrivateToOrganization == false ||
                                 CurrentUser.OrganizationIds.Contains(pi.Problem.OrganizationId))
                            select pi;

                return (from pi in query
                        select new ProblemIndexItemModel()
                                   {
                                       Id = pi.ProblemId,
                                       Text = pi.Problem.Text
                                   }).ToList();
            }
        }

        public List<ProblemIndexItemModel> GetIssueProblems(string issueId)
        {
            using (var session = votingSessionFactory.CreateContext())
            {
                return (from pi in session.ProblemIssues
                        where pi.IssueId == issueId && (pi.Problem.IsPrivateToOrganization == false || CurrentUser.OrganizationIds.Contains(pi.Problem.OrganizationId))
                        select new ProblemIndexItemModel()
                        {
                            Id = pi.ProblemId,
                            Text = pi.Problem.Text
                        }).ToList();
            }
        }

        private bool GetIsViewedByCurrentUser(string creatorObjectId, string versionId, IEnumerable<Data.EF.Voting.IdeaVersionView> viewedVersions)
        {
            if (viewedVersions == null)
            {
                return true;
            }

            if (creatorObjectId == CurrentUser.Id)
            {
                return true;
            }
            return viewedVersions.Where(v => v.IdeaVersionObjectId == versionId).Any();
        }

        private IEnumerable<WikiVersionModel> GetVersionOrder(IEnumerable<WikiVersionModel> versions)
        {
            return versions.OrderByDescending(v => v.SupportingUserCount).ThenByDescending(v => v.CreatedOn);
        }

        private IEnumerable<WikiTextVersionWithHistory> GetVersionOrder(IEnumerable<WikiTextVersionWithHistory> versions)
        {
            return versions.OrderByDescending(v => v.SupportingUserCount).ThenByDescending(v => v.CreatedOn);
        }

        private WikiVersionModel GetCurrentVersion(IdeaViewModel view)
        {
            if (!string.IsNullOrEmpty(view.FinalVersionId))
            {
                return view.Versions.Where(v => v.Id == view.FinalVersionId).SingleOrDefault();
            }

            var likedVersion = view.Versions.Where(v => v.IsLikedByCurrentUser).FirstOrDefault();
            if (likedVersion != null)
            {
                return likedVersion;
            }

            return GetVersionOrder(view.Versions).First();
        }

        private WikiTextVersionWithHistory GetCurrentVersion(Idea idea)
        {
            if (!string.IsNullOrEmpty(idea.FinalVersionId))
            {
                return idea.SummaryWiki.Versions.Where(v => v.Id == idea.FinalVersionId).SingleOrDefault();
            }

            return GetVersionOrder(idea.SummaryWiki.Versions).First();
        }

        private CommentView GetCommentViewFromComment(Comment comment, MongoObjectId parentId, Idea idea)
        {
            return commentService.GetCommentViewFromComment(idea.Id, comment, parentId, EntryTypes.Idea, idea.GetRelatedVersionNumber(comment.RelatedVersionId));
        }

        public Idea GetIdea(MongoObjectId ideaId)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                return noSqlSession.GetById<Idea>(ideaId);
            }
        }

        public void UpdateIdea(Idea idea, bool modifyDate = true)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                if (modifyDate)
                {
                    idea.ModificationDate = DateTime.Now;
                }

                noSqlSession.Update(idea);
            }
        }

        private void CacheVersions(IdeaViewModel idea)
        {
            cache.PutItem(GetCacheKey(idea.Id), idea.Versions, new string[0], new TimeSpan(0, 10, 0), DateTime.MaxValue);
        }

        private string GetSupportPercentageString(int count, int totalCount)
        {
            return totalCount > 0
                       ? ((float)count / totalCount).ToString("P")
                       : CommonStrings.NotLiked;
        }

        private void EnsureIdeaIndices()
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                noSqlSession.CreateIndex<Idea>("IX_Idea_RegistrationDate", false, "RegistrationDate");
            }
        }

        private IdeaViewModel GetIdeaViewInternal(MongoObjectId id, MongoObjectId versionId)
        {
            var idea = GetIdea(id);
            if (idea == null)
            {
                throw new ObjectNotFoundException();
            }

            return GetIdeaViewInternal(idea, versionId);
        }

        private IdeaViewModel GetIdeaViewInternal(Idea idea, MongoObjectId versionId)
        {
            var view = GetViewModelFromIdea(idea, versionId);

            CacheVersions(view);

            return view;
        }

        private void SetPageViewed(IdeaViewModel idea)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                noSqlSession.UpdateById<Idea>(idea.Id, Update.Inc("ViewsCount", 1));
            }

            if (CurrentUser.IsAuthenticated)
            {
                bus.Send(new IdeaCommand
                             {
                                 ActionType = ActionTypes.IdeaViewed,
                                 UserId = CurrentUser.Id,
                                 ObjectId = idea.Id,
                                 VersionId = idea.CurrentVersion.Id,
                                 IsPrivate = idea.IsPrivateToOrganization,
                                 Link = Url.Action("Details", "Idea", new { id = idea.Id, VersionId = idea.CurrentVersion.Id })
                             });
            }
        }

        private void SetVersionViewed(MongoObjectId versionObjectId)
        {
            if (!CurrentUser.DbId.HasValue)
            {
                return;
            }

            bus.Send(new IdeaVersionViewedCommand
            {
                UserDbId = CurrentUser.DbId.Value,
                VersionId = versionObjectId.ToString()
            });
        }

        private IQueryable<Idea> GetSelectedCategoriesQuery(IEnumerable<SelectListItem> selectedCategories)
        {
            using (var session = noSqlSessionFactory())
            {
                List<BsonValue> arr = new List<BsonValue>();
                foreach (var item in selectedCategories)
                {
                    arr.Add(BsonValue.Create(Convert.ToInt32(item.Value)));
                }
                return session.GetAllIn<Idea>("CategoryIds", arr.ToArray());
            }
        }

        private IQueryable<Idea> GetSelectedCategoriesInversedQuery(IEnumerable<SelectListItem> selectedCategories)
        {
            using (var session = noSqlSessionFactory())
            {
                List<BsonValue> arr = new List<BsonValue>();
                foreach (var item in selectedCategories)
                {
                    arr.Add(BsonValue.Create(Convert.ToInt32(item.Value)));
                }
                return session.GetAllNotIn<Idea>("CategoryIds", arr.ToArray());
            }
        }

        public bool DeleteVersion(MongoObjectId ideaId, string versionId)
        {
            var idea = GetIdea(ideaId);
            if (idea.SummaryWiki.Versions.Count > 1)
            {
                var version = idea.SummaryWiki.Versions.Where(v => v.Id == versionId).SingleOrDefault();
                if (version != null)
                {
                    if (!IsVersionEditable(idea, version))
                    {
                        throw new UnauthorizedAccessException("This version is not editable");
                    }

                    idea.SummaryWiki.Versions.Remove(version);
                    cache.InvalidateItem(GetCacheKey(ideaId));
                    UpdateIdea(idea);
                }

                using (var votingSession = votingSessionFactory.CreateContext(true))
                {
                    votingSession.IdeaVersionViews.Delete(v => v.IdeaVersionObjectId == versionId);
                    votingSession.IdeaVersions.Delete(v => v.VersionId == versionId);
                }

                using (var actionSession = actionSessionFactory.CreateContext(true))
                {
                    var actions = actionSession.Actions.Where(a => a.RelatedObjectId == versionId);
                    foreach (var action in actions)
                    {
                        action.IsDeleted = true;
                    }
                }

                searchService.UpdateIndex(ideaId, EntryTypes.Idea);

                return true;
            }

            return false;
        }

        public UserListContainerModel GetSupporters(string ideaId, string versionId, int pageNumber)
        {
            var idea = GetIdea(ideaId);
            List<SupportingUser> supportingUsers = new List<SupportingUser>();
            if (string.IsNullOrEmpty(versionId))
            {
                foreach (var v in idea.SummaryWiki.Versions)
                {
                    foreach (var user in v.SupportingUsers)
                    {
                        if (user.Id == CurrentUser.Id)
                        {
                            supportingUsers.Insert(0, user);
                            versionId = v.Id;
                        }
                        else if (user.IsPublic)
                        {
                            supportingUsers.Add(user);
                        }
                    }
                }

                supportingUsers = supportingUsers.OrderByDescending(d => d.Date).GetExpandablePage(pageNumber, CustomAppSettings.PageSizeList).ToList();
            }
            else
            {
                var version = idea.SummaryWiki.Versions.Where(v => v.Id == versionId).SingleOrDefault();
                if (version == null)
                {
                    return null;
                }

                foreach (var user in version.SupportingUsers)
                {
                    if (user.Id == CurrentUser.Id)
                    {
                        supportingUsers.Insert(0, user);
                    }
                    else if (user.IsPublic)
                    {
                        supportingUsers.Add(user);
                    }
                }

                supportingUsers = supportingUsers.OrderByDescending(d => d.Date).GetExpandablePage(pageNumber, CustomAppSettings.PageSizeList).ToList();
            }

            var simpleList = new UserListContainerModel();
            var list = new List<UserListModel>();
            foreach (var user in supportingUsers)
            {
                list.Add(new UserListModel()
                             {
                                 Id = user.Id,
                                 FullName = user.FullName,
                                 IsCurrent = CurrentUser.IsAuthenticated && user.Id == CurrentUser.Id,
                                 IsPublic = user.IsPublic,
                                 SetIsPublicUrl = Url.Action("SetIsPublic", "Idea",
                                                     new { ideaId = ideaId, userObjectId = user.Id, versionId = versionId }),
                                 Date = user.Date
                             });
            }

            simpleList.List = new ExpandableList<UserListModel>(list, CustomAppSettings.PageSizeList);
            return simpleList;
        }

        public UserListContainerModel GetUniqueSupporters(string ideaId, int pageNumber)
        {
            List<UserListModel> list;
            using (var session = votingSessionFactory.CreateContext())
            {
                list =
                session.IdeaVotes.Where(v => v.IdeaId == ideaId && v.IsPublic && (!CurrentUser.IsUnique || v.PersonCode != CurrentUser.PersonCode)).OrderBy(v => v.Date).Select(v => new UserListModel()
                {
                    Id = v.UserObjectId,
                    FullName = v.FirstName + " " + v.LastName,
                    IsCurrent = CurrentUser.IsUnique && v.PersonCode == CurrentUser.PersonCode,
                    PersonCode = v.PersonCode,
                    Date = v.Date
                }).OrderByDescending(v => v.Date).GetExpandablePage(
                    pageNumber, CustomAppSettings.PageSizeList).ToList();
                if (CurrentUser.IsUnique && pageNumber == 0)
                {
                    var currentUsr = session.IdeaVotes.Where(
                        v => v.IdeaId == ideaId && v.PersonCode == CurrentUser.PersonCode).Select(
                            v => new UserListModel()
                                     {
                                         Id = v.UserObjectId,
                                         FullName = v.FirstName + " " + v.LastName,
                                         IsCurrent = true,
                                         PersonCode = v.PersonCode,
                                         IsPublic = v.IsPublic,
                                         Date = v.Date
                                     }).SingleOrDefault();
                    if (currentUsr != null)
                    {
                        list.Insert(0, currentUsr);
                    }
                }

                if (pageNumber == 0)
                {
                    var someDaysAgo = DateTime.Now.Date.AddDays(-2);
                    var lastDaysVotesCount = from v in session.IdeaVotes
                                             where v.IdeaId == ideaId && v.Date > someDaysAgo
                                             group v by new { v.Date.Year, v.Date.Month, v.Date.Day }
                                                 into g
                                                 select new { Date = g.Key, Count = g.Count() };

                    foreach (var lastDay in lastDaysVotesCount)
                    {
                        list.Insert(0, new UserListModel()
                        {
                            FullName = GetDayString(lastDay.Date.Year, lastDay.Date.Month, lastDay.Date.Day)
                                + ": " + GlobalizedSentences.GetVotesCountString(lastDay.Count)
                        });
                    }
                }
            }

            foreach (var user in list)
            {
                if (CurrentUser.IsUnique && CurrentUser.PersonCode == user.PersonCode)
                {
                    user.SetIsPublicUrl = Url.Action("SetIsPublicUnique", "Idea",
                                                     new { ideaId = ideaId, personCode = user.PersonCode });
                }
            }

            return new UserListContainerModel()
                       {
                           List = new ExpandableList<UserListModel>(list, CustomAppSettings.PageSizeList)
                       };
        }

        private string GetDayString(int year, int month, int day)
        {
            var date = new DateTime(year, month, day);
            if (date == DateTime.Now.Date)
            {
                return Days.Today;
            }

            if (date == DateTime.Now.Date.AddDays(-1))
            {
                return Days.Yesterday;
            }

            if (date == DateTime.Now.Date.AddDays(-2))
            {
                return Days.BeforeYesterday;
            }

            return date.ToShortDateString();
        }

        private string GetUserFullName(MongoObjectId id)
        {
            using (var session = noSqlSessionFactory())
            {
                var user = session.Find<Data.MongoDB.User>(Query.EQ("_id", id)).SetFields("FirstName", "LastName").SingleOrDefault();
                if (user != null)
                {
                    return user.FullName;
                }

                return string.Empty;
            }
        }

        public PrioritizerModel GetIdeasForPrioritizer()
        {
            List<string> likedIdeas;
            using (var actionSession = actionSessionFactory.CreateContext())
            {
                likedIdeas = (from a in actionSession.Actions
                              where a.UserId == CurrentUser.DbId && a.ActionTypeId == (int)ActionTypes.IdeaVersionLiked
                              select a.ObjectId).Distinct().ToList();
            }

            List<PrioritizerItemModel> list = new List<PrioritizerItemModel>();
            using (var noSqlSession = noSqlSessionFactory())
            {
                List<BsonValue> bsonIds = new List<BsonValue>();
                foreach (var id in likedIdeas)
                {
                    bsonIds.Add(BsonObjectId.Create(id));
                }
                var ideas = noSqlSession.GetAllIn<Data.MongoDB.Idea>("_id", bsonIds.ToArray())
                            .OrderByDescending(i => i.SummaryWiki.Versions.Sum(v => v.SupportingUserCount))
                            .Take(10).ToList();
                foreach (var idea in ideas)
                {
                    if (idea != null && !idea.CategoryIds.Contains(20)) //do not include Lietuva 2.0 stuff
                    {
                        list.Add(new PrioritizerItemModel()
                                     {
                                         Id = idea.Id,
                                         Subject = idea.Subject
                                     });
                    }
                }
            }

            var model = new PrioritizerModel
                            {
                                Items = (from first in list
                                         from second in list
                                         where first.Id.CompareTo(second.Id) > 0
                                         select new PrioritizerPair()
                                                    {
                                                        First = first,
                                                        Second = second
                                                    }).ToList()
                            };

            return model;
        }

        public PrioritizerResultModel SavePrioritizedIdeas(PrioritizerModel model)
        {
            PrioritizerResultModel result;
            using (var noSqlSession = noSqlSessionFactory())
            {
                result = new PrioritizerResultModel()
                           {
                               Items = (from m in model.Items
                                        where !string.IsNullOrEmpty(m.SelectedId)
                                        group m by m.SelectedId
                                            into g
                                            let idea =
                                                noSqlSession.Find<Idea>(Query.EQ("_id", new BsonObjectId(g.Key))).SetFields(
                                                    "Subject").SingleOrDefault()
                                            let totalCount = (from i in model.Items
                                                              where i.First.Id == g.Key || i.Second.Id == g.Key
                                                              select 1).Count()
                                            select
                                                new PrioritizerResultItemModel()
                                                    {
                                                        Id = g.Key,
                                                        Subject = idea.Subject,
                                                        Percent = (g.Count() / (float)totalCount),
                                                        PercentString = (g.Count() / (float)totalCount).ToString("p"),
                                                        PriorityNumber = g.Count(),
                                                        TotalNumber = totalCount
                                                    }).OrderByDescending(r => r.Percent).ToList()
                           };

                result.TotalItems = GetTotalResults();
            }

            using (var votingSession = votingSessionFactory.CreateContext(true))
            {
                using (var scope = new TransactionScope())
                {
                    votingSession.IdeaPriorities.Delete(p => p.UserId == CurrentUser.DbId);
                    votingSession.SaveChanges();

                    foreach (var p in result.Items)
                    {
                        votingSession.IdeaPriorities.Add(new Data.EF.Voting.IdeaPriority()
                                              {
                                                  IdeaObjectId = p.Id,
                                                  PriorityNumber = p.PriorityNumber,
                                                  TotalNumber = p.TotalNumber,
                                                  UserId = CurrentUser.DbId.Value
                                              });
                    }

                    votingSession.SaveChanges();
                    scope.Complete();
                }
            }

            return result;
        }

        public PrioritizerResultModel GetPrioritizedIdeas()
        {
            using (var votingSession = votingSessionFactory.CreateContext())
            {
                var totalList = GetTotalResults();

                var model = new PrioritizerResultModel { TotalItems = totalList };

                if (CurrentUser.IsAuthenticated)
                {
                    var list = (from p in votingSession.IdeaPriorities
                                where p.UserId == CurrentUser.DbId
                                group p by new { Id = p.IdeaObjectId, Subject = p.Idea.Subject }
                                    into g
                                    select new PrioritizerResultItemModel()
                                               {
                                                   Id = g.Key.Id,
                                                   Subject = g.Key.Subject,
                                                   Percent =
                                                       g.Sum(v => v.PriorityNumber) /
                                                       (float)g.Sum(v => v.TotalNumber),
                                               }).OrderByDescending(p => p.Percent).ToList();

                    list.ForEach(l => l.PercentString = l.Percent.ToString("p"));
                    model.Items = list;
                }

                return model;
            }
        }

        public bool UpdateIdeaDb()
        {
            using (var session = noSqlSessionFactory())
            {
                //using (var votingSession = votingSessionFactory.CreateContext(true))
                //{
                //    foreach (var idea in session.GetAll<Data.MongoDB.Idea>())
                //    {
                //        UpdateDbIdea(idea);
                //    }

                //    votingSession.SaveChanges();

                //    foreach (var idea in session.GetAll<Data.MongoDB.Idea>())
                //    {
                //        UpdateDbIdeaFinalVersion(idea);
                //    }
                //}
                foreach (var idea in session.GetAll<Data.MongoDB.Idea>())
                {
                    //foreach (var version in idea.SummaryWiki.Versions)
                    //{
                    //    foreach (var like in version.SupportingUsers)
                    //    {
                    //        var dbUser = UserService.GetUser(like.Id);
                    //        like.PersonCode = dbUser != null ? dbUser.PersonCode : null;
                    //    }
                    //}
                    idea.ModificationDate = idea.RegistrationDate;
                    UpdateIdea(idea, false);
                }
            }

            return true;
        }

        public bool UpdateCommentNumbers()
        {
            using (var session = noSqlSessionFactory())
            {
                foreach (var idea in session.GetAll<Data.MongoDB.Idea>().OrderBy(i => i.RegistrationDate))
                {
                    idea.LastNumber = 0;
                    foreach (var comment in idea.Comments)
                    {
                        idea.LastNumber++;
                        comment.Number = idea.LastNumber + ".";
                        comment.LastNumber = 0;
                        foreach (var ccomment in comment.Comments.OrderBy(c => c.Date))
                        {
                            comment.LastNumber++;
                            ccomment.Number = comment.Number + comment.LastNumber;
                        }
                    }

                    session.Update(idea);
                }

                foreach (var idea in session.GetAll<Data.MongoDB.Issue>().OrderBy(i => i.RegistrationDate))
                {
                    idea.LastNumber = 0;
                    foreach (var comment in idea.Comments)
                    {
                        idea.LastNumber++;
                        comment.Number = idea.LastNumber + ".";
                        comment.LastNumber = 0;
                        foreach (var ccomment in comment.Comments.OrderBy(c => c.Date))
                        {
                            comment.LastNumber++;
                            ccomment.Number = comment.Number + comment.LastNumber;
                        }
                    }

                    session.Update(idea);
                }

                foreach (var idea in session.GetAll<Data.MongoDB.Problem>().OrderBy(i => i.Date))
                {
                    idea.LastNumber = 0;
                    foreach (var comment in idea.Comments)
                    {
                        idea.LastNumber++;
                        comment.Number = idea.LastNumber + ".";
                        comment.LastNumber = 0;
                        foreach (var ccomment in comment.Comments.OrderBy(c => c.Date))
                        {
                            comment.LastNumber++;
                            ccomment.Number = comment.Number + comment.LastNumber;
                        }
                    }

                    session.Update(idea);
                }
                foreach (var idea in session.GetAll<Data.MongoDB.User>().OrderBy(i => i.MemberSince))
                {
                    idea.LastNumber = 0;
                    foreach (var comment in idea.Comments)
                    {
                        idea.LastNumber++;
                        comment.Number = idea.LastNumber + ".";
                        comment.LastNumber = 0;
                        foreach (var ccomment in comment.Comments.OrderBy(c => c.Date))
                        {
                            comment.LastNumber++;
                            ccomment.Number = comment.Number + comment.LastNumber;
                        }
                    }

                    session.Update(idea);
                }
            }

            return true;
        }

        public void UpdateDbIdea(string id, string userId)
        {
            UpdateDbIdea(GetIdea(id), userId);
        }

        private void UpdateDbIdea(Data.MongoDB.Idea idea)
        {
            UpdateDbIdea(idea, CurrentUser.Id);
        }

        public void UpdateDbIdea(Data.MongoDB.Idea idea, string userId)
        {
            using (var votingSession = votingSessionFactory.CreateContext(true))
            {
                string ideaId = idea.Id.ToString();
                var dbIdea = votingSession.Ideas.SingleOrDefault(i => i.Id == ideaId);
                if (dbIdea == null)
                {
                    dbIdea = new Data.EF.Voting.Idea()
                        {
                            Id = idea.Id
                        };
                    votingSession.Ideas.Add(dbIdea);
                }

                dbIdea.Aim = idea.Aim;
                dbIdea.CreatedOn = idea.RegistrationDate.Truncate();
                dbIdea.ModifiedOn = (idea.ModificationDate ?? idea.RegistrationDate).Truncate();
                dbIdea.IsDraft = idea.IsDraft;
                dbIdea.Subject = idea.Subject;
                dbIdea.UserObjectId = idea.UserObjectId;

                dbIdea.StateId = (int)idea.ActualState;
                dbIdea.RequiredVotes = idea.RequiredVotes;
                dbIdea.IsImpersonal = idea.IsImpersonal;
                dbIdea.IsPrivate = idea.IsPrivateToOrganization;
                dbIdea.OrganizationId = idea.OrganizationId;
                dbIdea.FinalIdeaText = idea.Resolution;
                dbIdea.FinalVersionId = idea.FinalVersionId;

                var dbCategories = votingSession.IdeaCategories.Where(ic => ic.IdeaId == ideaId).ToList();
                var deletedCategoryIds = dbCategories.Select(
                    ic => ic.CategoryId).Except(idea.CategoryIds.Select(ri => ri)).ToList();
                if (deletedCategoryIds.Any())
                {
                    votingSession.IdeaCategories.Delete(
                        c => deletedCategoryIds.Contains(c.CategoryId) && c.IdeaId == ideaId);
                }

                foreach (var categoryId in idea.CategoryIds)
                {
                    if (!dbCategories.Any(c => c.CategoryId == categoryId))
                    {
                        dbIdea.IdeaCategories.Add(new IdeaCategory()
                        {
                            CategoryId = categoryId
                        });
                    }
                }

                foreach (var version in idea.SummaryWiki.Versions)
                {
                    string versionId = version.Id.ToString();
                    var dbVersion =
                        votingSession.IdeaVersions.SingleOrDefault(
                            v => v.IdeaId == ideaId && v.VersionId == versionId);
                    if (dbVersion == null)
                    {
                        dbVersion = new Data.EF.Voting.IdeaVersion()
                            {
                                VersionId = version.Id
                            };
                        dbIdea.IdeaVersions.Add(dbVersion);
                    }
                    dbVersion.CreatedOn = version.CreatedOn.Truncate();
                    dbVersion.ModifiedOn = (dbVersion.ModifiedOn == DateTime.MinValue
                                               ? version.CreatedOn
                                               : dbVersion.ModifiedOn).Truncate();
                    dbVersion.UserObjectId = version.CreatorObjectId;
                    dbVersion.VersionSubject = version.Subject;
                    dbVersion.OrganizationId = version.OrganizationId;
                    dbVersion.Text = version.Text;
                }
                foreach (var deletedVersion in votingSession.IdeaVersions.Where(v => v.IdeaId == ideaId))
                {
                    deletedVersion.IsDeleted =
                        idea.SummaryWiki.Versions.All(v => v.Id.ToString() != deletedVersion.VersionId);
                }

                idea.Comments.RemoveAll(c => string.IsNullOrEmpty(c.Text));
                if (!idea.IsClosed)
                {
                    idea.VotesCount = votingSession.IdeaVotes.Count(i => i.IdeaId == ideaId);
                }

                UpdateIdea(idea);

                votingSession.SaveChanges();

                commentService.UpdateComments(idea.Id, idea.Comments, EntryTypes.Idea);
            }
        }

        private void SaveDbRelatedIdeas(Idea idea, string userId)
        {
            using (var votingSession = votingSessionFactory.CreateContext(true))
            {
                string ideaId = idea.Id;
                votingSession.RelatedIdeas.Delete<RelatedIdea>(ic => ic.RelatedIdeaObjectId == ideaId || ic.IdeaObjectId == ideaId);
                foreach (var relatedIdeaId in idea.RelatedIdeas)
                {
                    var dbIdea = new Data.EF.Voting.RelatedIdea()
                        {
                            RelatedIdeaObjectId = relatedIdeaId,
                            UserObjectId = userId,
                            IdeaObjectId = ideaId
                        };

                    votingSession.RelatedIdeas.Add(dbIdea);
                }
            }
        }

        private void UpdateDbIdeaFinalVersion(Data.MongoDB.Idea idea)
        {
            using (var votingSession = votingSessionFactory.CreateContext(true))
            {
                string ideaId = idea.Id.ToString();
                var dbIdea = votingSession.Ideas.Single(i => i.Id == ideaId);
                dbIdea.FinalIdeaText = idea.Resolution;
                dbIdea.FinalVersionId = idea.FinalVersionId;
            }
        }

        private List<PrioritizerResultItemModel> GetTotalResults()
        {
            using (var votingSession = votingSessionFactory.CreateContext())
            {
                var list = (from p in votingSession.IdeaPriorities
                            group p by new { Id = p.IdeaObjectId, Subject = p.Idea.Subject }
                                into g
                                select new PrioritizerResultItemModel()
                                           {
                                               Id = g.Key.Id,
                                               Subject = g.Key.Subject,
                                               Percent =
                                                   g.Sum(v => v.PriorityNumber) /
                                                   (float)g.Sum(v => v.TotalNumber),
                                           }).OrderByDescending(p => p.Percent).ToList();

                list.ForEach(l => l.PercentString = l.Percent.ToString("p"));
                return list;
            }
        }

        public IEnumerable<SelectListItem> GetSelectedCategories(IEnumerable<int> selectedCategories)
        {
            var list = new List<SelectListItem>();
            list.Add(new SelectListItem { Selected = false, Text = "(" + CommonStrings.AllCategories + ")", Value = "0" });
            if (selectedCategories != null)
            {
                var categories = categoryService.GetCategories();
                foreach (var item in categories)
                {
                    list.Add(new SelectListItem
                                 {
                                     Selected = selectedCategories.Contains(item.ValueInt),
                                     Text = item.Text,
                                     Value = item.Value
                                 });
                }

                return list;
            }


            list[0].Selected = true;
            foreach (var item in categoryService.GetCategories())
            {
                list.Add(new SelectListItem { Selected = true, Text = item.Text, Value = item.Value });
            }

            return list;
        }

        public IEnumerable<SelectListItem> GetSelectedStates(IEnumerable<string> selectedStates)
        {
            var list = new List<SelectListItem>();
            list.Add(new SelectListItem { Selected = false, Text = "(" + CommonStrings.AllStates + ")", Value = "-1" });
            var states = new EnumList<IdeaStates>(IdeaStatesResource.ResourceManager).Items;
            list.AddRange(states);
            list.ForEach(a => a.Selected = selectedStates != null ? selectedStates.Contains(a.Value) : (IdeaStates)Enum.Parse(typeof(IdeaStates), a.Value) != IdeaStates.Closed);
            list[0].Selected = list.All(l => l.Value == "-1" || l.Selected);
            return list;
        }

        public void LikeCategories(IList<int> selectedCategoryIds)
        {
            categoryService.SaveMyCategories(selectedCategoryIds);
        }

        public string Join(string ideaId)
        {
            var idea = GetIdea(ideaId);
            Project project = null;

            using (var noSqlSession = noSqlSessionFactory())
            {
                if (idea.ProjectId != null)
                {
                    project = noSqlSession.GetById<Project>(idea.ProjectId);
                    if (!project.IsPrivate)
                    {
                        if (!project.ProjectMembers.Any(p => p.UserObjectId == CurrentUser.Id))
                        {
                            project.ProjectMembers.Add(new ProjectMember() { UserObjectId = CurrentUser.Id });
                        }
                    }
                    else
                    {
                        if (!project.UnconfirmedMembers.Any(p => p.UserObjectId == CurrentUser.Id))
                        {
                            project.UnconfirmedMembers.Add(new ProjectMember() { UserObjectId = CurrentUser.Id });
                        }
                    }

                    noSqlSession.Update(project);
                }
                else
                {
                    project = new Project()
                                      {
                                          IdeaId = ideaId,
                                          Id = BsonObjectId.GenerateNewId()
                                      };

                    project.ProjectMembers.Add(new ProjectMember() { UserObjectId = CurrentUser.Id });

                    noSqlSession.Add(project);
                    idea.ProjectId = project.Id;
                    noSqlSession.CreateIndex<Project>("IX_Project_IdeaId", true, "IdeaId");
                    if (idea.ActualState == IdeaStates.New)
                    {
                        idea.State = IdeaStates.Implementation;
                        bus.Send(new IdeaCommand()
                        {
                            ActionType = ActionTypes.IdeaStateChanged,
                            ObjectId = idea.Id,
                            UserId = CurrentUser.Id,
                            Text = IdeaStatesResource.ResourceManager.GetString(idea.ActualState.ToString()),
                            IsPrivate = idea.IsPrivateToOrganization,
                            Link = Url.Action("Details", "Idea", new { id = idea.Id })
                        });
                    }

                    noSqlSession.Update(idea);
                }
            }

            bus.Send(new ProjectCommand()
                         {
                             ActionType = ActionTypes.JoinedProject,
                             ProjectId = project.Id,
                             UserId = CurrentUser.Id,
                             ProjectSubject = idea.Subject,
                             Link = Url.RouteUrl("Project", new RouteValueDictionary
                                               {
                                                   {"controller", "Project"},
                                                   {"projectId", project.Id}
                                               }),
                             IsPrivate = idea.IsPrivateToOrganization
                         });

            return project.Id;
        }

        public void ChangeState(string ideaId, IdeaStates nextStateId, string stateDescription = null, bool depersonate = false)
        {
            var idea = GetIdea(ideaId);
            var initialState = idea.ActualState;
            idea.State = nextStateId;
            idea.StateDescription = stateDescription.RemoveNewLines().Sanitize();
            idea.IsImpersonal = depersonate;
            UpdateIdea(idea);

            if (initialState != idea.ActualState)
            {
                bus.Send(new IdeaCommand()
                {
                    ActionType = ActionTypes.IdeaStateChanged,
                    ObjectId = idea.Id,
                    UserId = CurrentUser.Id,
                    Text = IdeaStatesResource.ResourceManager.GetString(idea.ActualState.ToString()) + (!string.IsNullOrEmpty(idea.StateDescription) ? ": " + idea.StateDescription : string.Empty),
                    IsPrivate = idea.IsPrivateToOrganization,
                    Link = Url.Action("Details", "Idea", new { id = idea.Id })
                });
            }
        }

        public bool ResolveIdea(FinalIdeaModel model)
        {
            if (!CurrentUser.IsUnique)
            {
                throw new UserNotUniqueException();
            }

            var idea = GetIdea(model.IdeaId);

            if (CurrentUser.Role != UserRoles.Admin)
            {
                if (!CanCurrentUserEdit(idea))
                {
                    return false;
                }

                if (string.IsNullOrEmpty(model.FinalVersionId))
                {
                    if (idea.SummaryWiki.Versions.Sum(v => v.SupportingUserCount) * 100 < model.RequiredVotes)
                    {
                        return false;
                    }
                }
                else if (idea.SummaryWiki.Versions.Where(v => v.Id == model.FinalVersionId).Sum(v => v.SupportingUserCount) * 100 < model.RequiredVotes)
                {
                    return false;
                }
            }

            var resolution = (model.Resolution ?? string.Empty).Trim();

            idea.Resolution = resolution.RemoveNewLines().Sanitize();
            idea.RequiredVotes = model.RequiredVotes;
            idea.FinalVersionId = model.FinalVersionId;
            idea.InitiativeType = model.InitiativeType;
            idea.OfficialUrl = model.OfficialUrl;
            if (model.Deadline.HasValue)
            {
                idea.Deadline = model.Deadline;
                if (model.DeadlineTime.HasValue)
                {
                    idea.Deadline = idea.Deadline.Value.Add(model.DeadlineTime.Value);
                }
            }
            else
            {
                idea.Deadline = null;
            }

            if ((!string.IsNullOrEmpty(model.FinalVersionId) || !string.IsNullOrEmpty(resolution)))
            {
                idea.State = IdeaStates.Resolved;
            }
            if (idea.ActualState == IdeaStates.Resolved && string.IsNullOrEmpty(resolution) && string.IsNullOrEmpty(model.FinalVersionId))
            {
                idea.State = IdeaStates.New;
                idea.InitiativeType = null;
                idea.RequiredVotes = null;
                idea.Deadline = null;
                idea.RequiredVotes = null;
            }

            UpdateIdea(idea);

            bus.Send(new IdeaCommand()
                         {
                             ActionType = ActionTypes.IdeaStateChanged,
                             ObjectId = idea.Id,
                             UserId = CurrentUser.Id,
                             Text = IdeaStatesResource.ResourceManager.GetString(idea.State.ToString()) + (!string.IsNullOrEmpty(idea.Resolution) ? ": " + idea.Resolution : string.Empty),
                             IsPrivate = idea.IsPrivateToOrganization,
                             Link = Url.Action("Details", "Idea", new { id = idea.Id })
                         });
            if (idea.ActualState == IdeaStates.Resolved)
            {
                if (string.IsNullOrEmpty(idea.ResolvedByPersonCode))
                {
                    idea.ResolvedByPersonCode = CurrentUser.PersonCode;
                    UpdateIdea(idea);
                }

                if (model.SendMail)
                {
                    bus.Send(new IdeaResolvedCommand()
                        {
                            IdeaId = idea.Id,
                            Link = Url.ActionAbsolute("Details", "Idea", new { id = idea.Id })
                        });
                }
            }

            return true;
        }

        public string GetIssueSubject(string id)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                return noSqlSession.Find<Issue>(Query.EQ("_id", BsonObjectId.Create(id))).SetFields("Subject").Single().Subject;
            }
        }

        public Issue GetIssue(string id)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                return noSqlSession.Find<Issue>(Query.EQ("_id", BsonObjectId.Create(id))).Single();
            }
        }

        private List<int> GetMyMunicipalities()
        {
            if (!CurrentUser.IsAuthenticated)
            {
                return new List<int>();
            }

            if (CurrentUser.Municipalities == null)
            {
                var muns = addressService.GetUserMunicipalities(CurrentUser.Id);
                CurrentUser.Municipalities = muns;
            }

            return CurrentUser.Municipalities;
        }

        public IList<LabelValue> GetMatchedIdeas(string prefix)
        {
            using (var session = votingSessionFactory.CreateContext())
            {
                return (from i in session.Ideas
                        where i.Subject.Contains(prefix) && !i.IsDraft && (!i.IsPrivate || CurrentUser.OrganizationIds.Contains(i.OrganizationId))
                        select new LabelValue { label = i.Subject, value = i.Subject, id = i.Id }).Take(20).ToList();
            }
        }

        public List<SimpleListModel> GetIdeasList(IdeaListViews view, IdeaListSorts sort, IEnumerable<int> categories, IEnumerable<string> states, string organizationId)
        {
            var list = GetIdeaPage(0, int.MaxValue - 1, view, sort, categories, organizationId, states);
            if (!list.Items.List.Any())
            {
                list = GetIdeaPage(0, int.MaxValue - 1, view, sort, null, organizationId, null);
            }

            return list.Items.List.Select(l => new SimpleListModel()
                                                   {
                                                       Id = l.Id,
                                                       Subject = l.Subject
                                                   }).ToList();
        }

        public bool SetIsPublicUnique(string ideaId, string personCode, bool isPublic)
        {
            if (CurrentUser.PersonCode != personCode)
            {
                return false;
            }
            string userObjectId = string.Empty;
            using (var session = votingSessionFactory.CreateContext(true))
            {
                var vote =
                    session.IdeaVotes.Single(v => v.PersonCode == personCode && v.IdeaId == ideaId);
                vote.IsPublic = isPublic;
                userObjectId = vote.UserObjectId;
            }

            using (var session = actionSessionFactory.CreateContext(true))
            {
                var actions =
                    session.Actions.Where(
                        a =>
                        a.ActionTypeId == (int)ActionTypes.IdeaVersionLiked && a.UserObjectId == userObjectId &&
                        a.ObjectId == ideaId).ToList();

                foreach (var action in actions)
                {
                    action.IsDeleted = !isPublic;
                }
            }

            return true;
        }

        public bool SetIsPublic(string ideaId, string versionId, string userObjectId, bool isPublic)
        {
            if (CurrentUser.Id != userObjectId)
            {
                return false;
            }

            var idea = GetIdea(ideaId);
            var version = idea.SummaryWiki.Versions.SingleOrDefault(v => v.Id == versionId);
            if (version == null)
            {
                return false;
            }

            var user = version.SupportingUsers.SingleOrDefault(u => u.Id == userObjectId);
            if (user != null)
            {
                user.IsPublic = isPublic;
            }
            else
            {
                return false;
            }

            UpdateIdea(idea);

            using (var session = actionSessionFactory.CreateContext(true))
            {
                var actions =
                    session.Actions.Where(
                        a =>
                        a.ActionTypeId == (int)ActionTypes.IdeaVersionLiked && a.UserObjectId == userObjectId &&
                        a.ObjectId == ideaId).ToList();
                foreach (var action in actions)
                {
                    action.IsDeleted = !isPublic;
                }
            }

            return true;
        }

        public void MakePublic(string id)
        {
            var idea = GetIdea(id);
            idea.Visibility = ObjectVisibility.Public;
            idea.IsMailSent = false;
            UpdateIdea(idea);
            var command = new IdeaCommand
            {
                ActionType = ActionTypes.IdeaCreated,
                UserId = CurrentUser.Id,
                ObjectId = idea.Id.ToString(),
                Link =
                    Url.ActionAbsolute("Details", "Idea",
                                       new { id = idea.Id, subject = idea.Subject.ToSeoUrl() }),
                IsPrivate = idea.IsPrivateToOrganization,
                SendMail = false
            };

            bus.Send(command);
        }

        public void PromoteToFrontPage(string id, bool promote)
        {
            var idea = GetIdea(id);
            idea.PromoteToFrontPage = promote;
            UpdateIdea(idea);
        }

        public RelatedIdeaDialogModel GetRelatedIdeaDialogModel(string id)
        {
            return new RelatedIdeaDialogModel()
                {
                    Id = id,
                    RelatedIdeas = GetRelatedIdeas(id)
                };
        }

        public RelatedIssueDialogModel GetRelatedIssueDialogModel(string id)
        {
            return new RelatedIssueDialogModel()
            {
                Id = id,
                RelatedIssues = GetRelatedIssues(id)
            };
        }

        public Data.ViewModels.Voting.VersionViewModel GetHistoryVersion(string id, string versionId, string historyId)
        {
            return (from v in GetVersions(id)
                    from h in v.History
                    where v.Id == versionId && h.Id == historyId
                    select new Data.ViewModels.Voting.VersionViewModel()
                    {
                        Text = h.Text,
                        Subject = h.Subject,
                        IsForHistory = true
                    }).Single();
        }

        public string CompareHistoryVersions(string id, string versionId, string historyId1, string historyId2)
        {
            var text1 = (from v in GetVersions(id)
                         from h in v.History
                         where v.Id == versionId && h.Id == historyId1
                         select h.Text).Single();
            var text2 = (from v in GetVersions(id)
                         from h in v.History
                         where v.Id == versionId && h.Id == historyId2
                         select h.Text).Single();

            //var diff = new diff_match_patch();
            //var d = diff.diff_main(text1, text2);
            //diff.diff_cleanupSemantic(d);
            //return diff.diff_prettyHtml(d);

            HtmlDiff diffHelper = new HtmlDiff(text1, text2);
            return diffHelper.Build();
        }
    }
}