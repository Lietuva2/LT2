using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity.Core;
using System.Linq;
using System.Transactions;
using System.Web;
using System.Web.Mvc;
using Bus.Commands;
using Data.EF.Actions;
using Data.EF.Voting;
using Data.Enums;
using Data.Infrastructure.Sessions;
using Data.MongoDB;
using Data.MongoDB.Interfaces;
using Data.ViewModels.Base;
using Data.ViewModels.Comments;
using Data.ViewModels.Voting;
using EntityFramework.Extensions;
using Framework;
using Framework.Data;
using Framework.Enums;
using Framework.Exceptions;
using Framework.Hashing;
using Framework.Infrastructure;
using Framework.Infrastructure.Logging;
using Framework.Mvc.Helpers;
using Framework.Infrastructure.Storage;
using Framework.Infrastructure.ValueInjections;
using Framework.Lists;
using Framework.Mvc.Lists;
using Framework.Mvc.Strings;
using Framework.Other;
using Framework.Strings;
using Globalization;
using Globalization.Resources.Services;
using Helpers;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Builders;

using Ninject;
using Omu.ValueInjecter;
using Services.Caching;
using Services.Classes;
using Services.Documents;
using Services.Enums;
using Services.Exceptions;
using Services.Infrastructure;
using Services.Session;
using Idea = Data.MongoDB.Idea;
using Issue = Data.MongoDB.Issue;
using Data.ViewModels.Problem;
using Framework.Bus;
using Comment = Data.MongoDB.Comment;
using ObjectVisibility = Data.Enums.ObjectVisibility;

namespace Services.ModelServices
{
    public class VotingService : BaseCommentableService, IService
    {
        private readonly IVotingContextFactory votingSessionFactory;
        private readonly IActionsContextFactory actionSessionFactory;
        private readonly Func<INoSqlSession> noSqlSessionFactory;
        private readonly CategoryService categoryService;
        private readonly AddressService addressService;
        private readonly SearchService searchService;
        private readonly ShortLinkService shortLinkService;

        private readonly IBus bus;
        private readonly ICache cache;
        private readonly ILogger logger;

        public UserService UserService { get { return ServiceLocator.Resolve<UserService>(); } }
        public ProblemService ProblemService { get { return ServiceLocator.Resolve<ProblemService>(); } }
        public IdeaService IdeaService { get { return ServiceLocator.Resolve<IdeaService>(); } }
        public OrganizationService OrganizationService { get { return ServiceLocator.Resolve<OrganizationService>(); } }
        public ActionService ActionService { get { return ServiceLocator.Resolve<ActionService>(); } }

        public UrlHelper Url
        {
            get { return new UrlHelper(((MvcHandler)HttpContext.Current.Handler).RequestContext); }
        }

        public VotingService(
            IVotingContextFactory votingSessionFactory,
            IActionsContextFactory actionSessionFactory,
            Func<INoSqlSession> noSqlSessionFactory,
            CategoryService categoryService,
            AddressService addressService,
            SearchService searchService,
            CommentService commentService,
            ShortLinkService shortLinkService,
            ICache cache,
            IBus bus,
            ILogger logger)
            : base(commentService)
        {
            this.votingSessionFactory = votingSessionFactory;
            this.noSqlSessionFactory = noSqlSessionFactory;
            this.actionSessionFactory = actionSessionFactory;
            this.categoryService = categoryService;
            this.addressService = addressService;
            this.searchService = searchService;
            this.shortLinkService = shortLinkService;
            this.bus = bus;
            this.cache = cache;
            this.logger = logger;
        }

        public IQueryable<Issue> GetAllIssues()
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                return noSqlSession.GetAll<Issue>();
            }
        }

        public SimpleListContainerModel GetVotedIssues(string userObjectId, int pageNumber)
        {
            using (var session = votingSessionFactory.CreateContext())
            {
                var result = (from i in session.Votes.Where(
                             a => a.UserObjectId == userObjectId && (!a.Issue.IsPrivateToOrganization || CurrentUser.OrganizationIds.Contains(a.Issue.OrganizationId)))
                              select new SimpleListModel
                  {
                      Id = i.Issue.ObjectId,
                      Subject = i.Issue.Subject,
                      Date = i.Date
                  }).OrderByDescending(r => r.Date)
                .GetExpandablePage(pageNumber, CustomAppSettings.PageSizeList).ToList();
                result.ForEach(r => r.Type = EntryTypes.Issue);
                var simpleList = new SimpleListContainerModel();
                simpleList.List = new ExpandableList<SimpleListModel>(result, CustomAppSettings.PageSizeList);
                return simpleList;
            }
        }

        public SimpleListContainerModel GetCommentedIssues(string userObjectId, int pageNumber)
        {
            using (var session = votingSessionFactory.CreateContext())
            {
                var result = (from ic in session.IssueComments
                              join i in session.Issues on ic.IssueId equals i.ObjectId
                              where ic.UserObjectId == userObjectId &&
                              (!i.IsPrivateToOrganization || CurrentUser.OrganizationIds.Contains(i.OrganizationId))
                              select new SimpleListModel
                  {
                      Id = i.ObjectId,
                      Subject = i.Subject,
                      Date = i.RegistrationDate
                  }).Distinct().OrderByDescending(r => r.Date).GetExpandablePage(pageNumber, CustomAppSettings.PageSizeList).ToList();
                result.ForEach(r => r.Type = EntryTypes.Issue);
                var simpleList = new SimpleListContainerModel();
                simpleList.List = new ExpandableList<SimpleListModel>(result, CustomAppSettings.PageSizeList);
                return simpleList;
            }
        }

        public SimpleListContainerModel GetCreatedIssues(string userObjectId, int pageNumber)
        {
            using (var session = votingSessionFactory.CreateContext())
            {
                var result = (from i in session.IssueVersions.Where(
                             a => a.UserObjectId == userObjectId && (!a.Issue.IsPrivateToOrganization || CurrentUser.OrganizationIds.Contains(a.Issue.OrganizationId)))
                              select new SimpleListModel
                              {
                                  Id = i.Issue.ObjectId,
                                  Subject = i.Issue.Subject,
                                  Date = i.Issue.RegistrationDate
                              }).Distinct().OrderByDescending(r => r.Date)
                .GetExpandablePage(pageNumber, CustomAppSettings.PageSizeList).ToList();
                result.ForEach(r => r.Type = EntryTypes.Issue);
                var simpleList = new SimpleListContainerModel();
                simpleList.List = new ExpandableList<SimpleListModel>(result, CustomAppSettings.PageSizeList);
                return simpleList;
            }
        }

        public VotingIndexModel GetTopIssues(int pageNumber)
        {
            return GetIssuePage(pageNumber, CustomAppSettings.PageSizeStartPage, IssueListViews.Interesting, IssueListSorts.Nearest, categoryService.GetCategories().Select(a => a.ValueInt), null, true);
        }

        public VotingResultsModel GetTopResults(int pageNumber)
        {
            return GetResultsPage(pageNumber, CustomAppSettings.PageSizeStartPage, IssueListViews.Interesting, IssueListSorts.Nearest, categoryService.GetCategories().Select(a => a.ValueInt), null);
        }

        public VotingIndexModel GetIssuePage(int pageNumber, IssueListViews? view, IssueListSorts sort, IEnumerable<int> selectedCategories, string organizationId, bool notVotedOnly = false)
        {
            return GetIssuePage(pageNumber, CustomAppSettings.PageSizeList, view, sort, selectedCategories,
                                organizationId, notVotedOnly);
        }

        public VotingIndexModel GetIssuePage(int pageNumber, int pageSize, IssueListViews? view, IssueListSorts sort, IEnumerable<int> selectedCategories, string organizationId, bool notVotedOnly = false)
        {
            EnsureIssueIndices();
            var model = new VotingIndexModel();

            if (CurrentUser.IsAuthenticated && selectedCategories == null)
            {
                selectedCategories = categoryService.GetMyCategoryIds();
            }

            model.SelectedCategories = GetSelectedCategories(selectedCategories);

            var query = GetIssuesQuery(view, sort, model.SelectedCategories, organizationId);
            //if (view == IssueListViews.Local)
            //{
            //    model.NoMunicipalities = GetMyMunicipalities().Count == 0;
            //}

            query = query.Where(i => (!i.Deadline.HasValue || i.Deadline.Value >= DateTime.Now) && i.OfficialVote == ForAgainst.Neutral);

            var userVotes = GetUserVotes();

            //if(CurrentUser.IsAuthenticated && notVotedOnly)
            //{
            //    query = query.Where(q => GetVoteForAgainst(userVotes, q.DbId) == ForAgainst.Neutral);
            //}

            model.TotalCount = query.Count();

            if (sort == IssueListSorts.Nearest)
            {
                if (notVotedOnly)
                {
                    query = query.OrderBy(i => GetVoteForAgainst(userVotes, i.DbId).HasValue).ThenBy(i => i.Deadline ?? DateTime.MaxValue).ThenByDescending(i => i.RegistrationDate);
                }
                else
                {
                    query = query.OrderBy(i => i.Deadline ?? DateTime.MaxValue).ThenByDescending(i => i.ModificationDate);
                }
            }
            else if (sort == IssueListSorts.MostActive)
            {
                if (notVotedOnly)
                {
                    query = query.OrderBy(i => GetVoteForAgainst(userVotes, i.DbId).HasValue).ThenByDescending(i => (i.SupportingVotesCount + i.NonSupportingVotesCount) * 2 + i.Comments.Count).ThenByDescending(i => i.ModificationDate);
                }
                else
                {
                    query = query.OrderByDescending(i => (i.SupportingVotesCount + i.NonSupportingVotesCount) * 2 + i.Comments.Count).ThenByDescending(i => i.ModificationDate);
                }

            }

            var result = query.GetExpandablePage(pageNumber, pageSize).Select(
                i => new VotingIndexItemModel
                         {
                             Id = i.Id,
                             DbId = i.DbId,
                             Subject = i.Subject,
                             DocumentUrl = i.DocumentUrl,
                             Deadline = i.Deadline,
                             TimeLeft = GlobalizedSentences.GetTimeLeftString(i.Deadline),
                             CategoryIds = i.CategoryIds,
                             VotesCount = i.SupportingVotesCount + i.NonSupportingVotesCount + i.NeutralVotesCount,
                             CommentsCount = i.Comments.Count,
                             ViewsCount = i.ViewsCount,
                             ActivityRank = (i.SupportingVotesCount + i.NonSupportingVotesCount + i.NeutralVotesCount) * 2 + i.Comments.Count,
                             Municipality = addressService.GetMunicipality(i.MunicipalityId),
                             Vote = GetVoteForAgainst(userVotes, i.DbId),
                             AdditionalVote = GetAdditionalVoteForAgainst(i),
                             IsPrivate = i.IsPrivateToOrganization,
                             Progress = new VotingStatisticsViewModel()
                                            {
                                                Id = i.Id,
                                                SupportPercentage = i.GetSupportPercentage() ?? 0,
                                                NonSupportingVotesCount = i.NonSupportingVotesCount,
                                                SupportingVotesCount = i.SupportingVotesCount,
                                                NeutralVotesCount = i.NeutralVotesCount,
                                                SupportingAdditionalVotesCount = i.AdditionalVotes.Count(v => v.ForAgainst == ForAgainst.For),
                                                NonSupportingAdditionalVotesCount = i.AdditionalVotes.Count(v => v.ForAgainst == ForAgainst.Against),
                                                NeutralAdditionalVotesCount = i.AdditionalVotes.Count(v => v.ForAgainst == ForAgainst.Neutral)
                                            },
                             Text = i.Summary.GetPlainText(1000, true),
                             Summary = i.Summary
                         }).ToList();

            foreach (var r in result)
            {
                r.Categories = r.CategoryIds.Select(cId => new TextValue() { Text = categoryService.GetCategoryName(cId), ValueInt = cId }).ToList();
                r.VoteButtonVisible = (!r.Vote.HasValue
                                       && !r.AdditionalVote.HasValue) ||
                                      (!CurrentUser.IsAuthenticated && !CurrentUser.IsUnique);
                r.Progress.VotesCount = r.VotesCount;
                if (!string.IsNullOrEmpty(organizationId) && !r.VoteButtonVisible)
                {
                    var vote = userVotes.SingleOrDefault(v => v.IssueId == r.DbId);
                    if (vote != null && !vote.ValidateSignature(r.Subject, r.Summary))
                    {
                        r.VoteButtonVisible = true;
                    }
                }
            }

            //if (sort == IssueListSorts.MostActive)
            //{
            //    result = result.OrderByDescending(i => i.ActivityRank).ToList();
            //}
            model.IsEditable = CurrentUser.IsAuthenticated;
            model.Items = new ExpandableList<VotingIndexItemModel>(result, pageSize);

            return model;
        }

        private IQueryable<Data.MongoDB.Issue> GetIssuesQuery(IssueListViews? view, IssueListSorts sort, IEnumerable<SelectListItem> selectedCategories, string organizationId)
        {
            IQueryable<Issue> query;
            if (string.IsNullOrEmpty(organizationId))
            {
                var categories = selectedCategories.Where(c => c.Selected).Select(c => Convert.ToInt32(c.Value));
                if (WorkContext.CategoryIds != null)
                {
                    categories = WorkContext.CategoryIds;
                }
                query = GetSelectedCategoriesQuery(categories);

                if (view.HasValue)
                {
                    if (WorkContext.Municipality == null)
                    {
                        var myMuns = GetMyMunicipalities();
                        if (myMuns.Any())
                        {
                            query =
                                query.Where(q => !q.MunicipalityId.HasValue || myMuns.Contains(q.MunicipalityId.Value));
                        }
                    }

                    if (view == IssueListViews.Other)
                    {
                        var ids = query.Select(q => BsonObjectId.Create(q.Id)).ToList();
                        using (var session = noSqlSessionFactory())
                        {
                            query = session.GetAllNotIn<Data.MongoDB.Issue>("_id", ids.ToArray());
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
                query = GetAllIssues().Where(i => i.OrganizationId == organizationId).ToList().AsQueryable();
            }

            if (!CurrentUser.OrganizationIds.Contains(organizationId))
            {
                query = query.Where(q => !q.IsPrivateToOrganization);
            }

            return query;
        }
        public VotingResultsModel GetResultsPage(int pageNumber, IssueListViews? view, IssueListSorts sort, IEnumerable<int> selectedCategories, string organizationId)
        {
            return GetResultsPage(pageNumber, CustomAppSettings.PageSizeList, view, sort, selectedCategories,
                                  organizationId);
        }

        public VotingResultsModel GetResultsPage(int pageNumber, int pageSize, IssueListViews? view, IssueListSorts sort, IEnumerable<int> selectedCategories, string organizationId)
        {
            var model = new VotingResultsModel();
            model.SelectedCategories = GetSelectedCategories(selectedCategories);

            var query = GetIssuesQuery(view, sort, model.SelectedCategories, organizationId);
            //if (view == IssueListViews.Local)
            //{
            //    model.NoMunicipalities = GetMyMunicipalities().Count == 0;
            //}

            query = query.Where(i => i.Deadline < DateTime.Now || i.OfficialVote != ForAgainst.Neutral);

            model.TotalCount = query.Count();

            if (sort == IssueListSorts.Nearest)
            {
                query = query.OrderByDescending(i => i.ModificationDate).ThenByDescending(i => i.Deadline);
            }
            else if (sort == IssueListSorts.MostActive)
            {
                query = query.OrderByDescending(i => (i.SupportingVotesCount + i.NonSupportingVotesCount) * 2 + i.Comments.Count).ThenByDescending(i => i.ModificationDate);
            }

            var result = query.GetExpandablePage(pageNumber, pageSize)
                .Select(
                    i => new VotingResultsItemModel
                             {
                                 Id = i.Id,
                                 Subject = i.Subject,
                                 DocumentUrl = i.DocumentUrl,
                                 Deadline = i.Deadline,
                                 TimePassed = GlobalizedSentences.GetTimePassedString(i.Deadline),
                                 CategoryIds = i.CategoryIds,
                                 VotesCount = i.SupportingVotesCount + i.NonSupportingVotesCount + i.NeutralVotesCount,
                                 CommentsCount = i.Comments.Count,
                                 ViewsCount = i.ViewsCount,
                                 ActivityRank = (i.SupportingVotesCount + i.NonSupportingVotesCount + i.NeutralVotesCount) * 2 + i.Comments.Count,
                                 Municipality = addressService.GetMunicipality(i.MunicipalityId),
                                 Progress = new VotingStatisticsViewModel()
                                 {
                                     Id = i.Id,
                                     SupportPercentage = i.GetSupportPercentage() ?? 0,
                                     NonSupportingVotesCount = i.NonSupportingVotesCount,
                                     SupportingVotesCount = i.SupportingVotesCount,
                                     Decision = i.GetDecision(),
                                     OfficialVote = i.OfficialVote,
                                     SupportingAdditionalVotesCount = i.AdditionalVotes.Count(v => v.ForAgainst == ForAgainst.For),
                                     NonSupportingAdditionalVotesCount = i.AdditionalVotes.Count(v => v.ForAgainst == ForAgainst.Against)
                                 },
                                 Text = i.Summary.GetPlainText(1000, true)
                             }).ToList();

            foreach (var r in result)
            {
                r.Categories = r.CategoryIds.Select(cId => new TextValue() { Text = categoryService.GetCategoryName(cId), ValueInt = cId }).ToList();
                r.Progress.VotesCount = r.VotesCount;
            }

            model.Items = new ExpandableList<VotingResultsItemModel>(result, pageSize);
            model.IsEditable = CurrentUser.IsAuthenticated;

            return model;
        }

        public VotingCreateEditModel GetIssueForEdit(MongoObjectId id)
        {
            var issue = GetIssue(id);
            var view = new VotingCreateEditModel();
            view.InjectFrom<UniversalInjection>(issue);
            view.Subject = issue.Subject.HtmlDecode();
            if (issue.Deadline.HasValue)
            {
                view.Deadline = issue.Deadline.Value.Date;
                view.DeadlineTime = issue.Deadline.Value.TimeOfDay;
            }
            view.CanCurrentUserEdit = issue.SummaryWiki.Versions.Select(v => v.CreatorObjectId.ToString()).Contains(issue.UserObjectId);
            view.CategoryIds = issue.CategoryIds;

            view.RelatedIdeas = GetRelatedIdeas(id);

            foreach (var item in issue.Urls)
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

            view.Ideas = GetMyIdeas(issue.CategoryIds, issue.OrganizationId);
            view.Categories = categoryService.GetCategories().ToSelectList().ToList();
            view.Municipalities = addressService.GetMunicipalities(1);
            view.OrganizationName = OrganizationService.GetOrganizationName(view.OrganizationId);
            view.Problems = GetIssueProblems(id);
            view.IsMailSendable = IsMailSendable(issue, true);

            return view;
        }

        public void Edit(VotingCreateEditModel model)
        {
            var issue = GetIssue(model.Id);
            if (issue == null || issue.Id.IsNullOrEmpty())
            {
                throw new Exception("Issue Id cannot be null");
            }
            issue.Deadline = model.Deadline;
            issue.Subject = model.Subject.RemoveNewLines().GetSafeHtml();
            issue.DocumentUrl = model.DocumentUrl;
            issue.AdditionalInfoRequiredForVoting = model.AdditionalInfoRequiredForVoting;
            issue.AllowSummaryWiki = model.AllowSummaryWiki;
            issue.AllowNeutralVotes = model.AllowNeutralVotes;
            issue.Visibility = model.Visibility;

            if (WorkContext.Municipality == null)
            {
                issue.MunicipalityId = model.MunicipalityId;
            }

            if (issue.MunicipalityId == 0)
            {
                issue.MunicipalityId = null;
            }
            if (model.DeadlineTime.HasValue)
            {
                issue.Deadline += model.DeadlineTime.Value;
            }


            if (WorkContext.CategoryIds != null)
            {
                issue.CategoryIds = WorkContext.CategoryIds;
            }
            else
            {
                issue.CategoryIds.Clear();
                issue.CategoryIds = model.CategoryIds;
            }
            model.Urls.BindUrls(issue.Urls);

            using (var votingSession = votingSessionFactory.CreateContext(true))
            {
                foreach (var problemId in model.Problems.Select(p => p.Id).Distinct())
                {
                    var pi =
                        votingSession.ProblemIssues.SingleOrDefault(
                            p => p.IssueId == model.Id && p.ProblemId == problemId);

                    if (pi == null)
                    {
                        pi = new Data.EF.Voting.ProblemIssue()
                            {
                                IssueId = issue.Id.ToString(),
                                ProblemId = problemId,
                                UserObjectId = CurrentUser.Id
                            };
                        votingSession.ProblemIssues.Add(pi);
                    }
                }

                var ideas = votingSession.IdeaIssues.Where(i => i.IssueObjectId == model.Id).ToList();
                foreach (var idea in model.RelatedIdeas)
                {
                    var ii = ideas.Where(i => i.IdeaObjectId == idea.ObjectId).SingleOrDefault();

                    if (ii == null)
                    {
                        ii = new IdeaIssue()
                                 {
                                     IdeaObjectId = idea.ObjectId,
                                     IssueObjectId = model.Id
                                 };

                        votingSession.IdeaIssues.Add(ii);
                    }

                    ii.ChangeState = idea.ChangeState;
                    if (idea.ChangeState)
                    {
                        ChangeRelatedIdeaStates(new List<string>() { idea.ObjectId });
                    }
                }

                foreach (var dbIdea in ideas.Where(i => !model.RelatedIdeas.Select(ri => ri.ObjectId).Contains(i.IdeaObjectId)))
                {
                    if (dbIdea.ChangeState)
                    {
                        using (var noSqlSession = noSqlSessionFactory())
                        {
                            var projectId =
                                noSqlSession.GetAll<Data.MongoDB.Idea>().Where(i => i.Id == dbIdea.IdeaObjectId).
                                    Select(i => i.ProjectId).Single();
                            ChangeRelatedIdeaStates(new List<string>() { dbIdea.IdeaObjectId },
                                                    projectId != null
                                                        ? IdeaStates.Implementation
                                                        : IdeaStates.New);
                        }
                    }

                    votingSession.IdeaIssues.Remove(dbIdea);
                }
            }

            UpdateIssue(issue);

            var command = new IssueCommand
            {
                ActionType = ActionTypes.IssueEdited,
                UserId = CurrentUser.Id,
                ObjectId = issue.Id.ToString(),
                IsPrivate = issue.IsPrivateToOrganization,
                Link = Url.Action("Details", "Voting", new { id = issue.Id }),
                SendMail = model.SendMail
            };

            bus.Send(command);
        }

        public VotingCreateEditModel Create(string organizationId = null, string relatedIdeaId = null, string problemId = null)
        {
            var model = new VotingCreateEditModel();

            if (organizationId == null)
            {
                model.Urls = new List<UrlEditModel>() { new UrlEditModel() };
            }

            model = FillCreateEditModel(model);

            if (!string.IsNullOrEmpty(organizationId))
            {
                model.Visibility = ObjectVisibility.Private;
                model.OrganizationId = organizationId;
                model.OrganizationName = OrganizationService.GetOrganizationName(organizationId);

                model.AllowSummaryWiki = false;
                model.AllowNeutralVotes = true;
            }

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
                        votingSession.ProblemIdeas.Where(pi => pi.ProblemId == problemId)
                            .Select(pi => new { pi.IdeaId, pi.Idea.Subject })
                            .ToList();
                    foreach (var problemIdea in otherProblemIdeas)
                    {
                        model.RelatedIdeas.Add(new RelatedIdeaListItem()
                        {
                            Name = problemIdea.Subject,
                            ObjectId = problemIdea.IdeaId
                        });
                    }
                }
            }

            if (!string.IsNullOrEmpty(relatedIdeaId))
            {
                var relatedIdea = IdeaService.GetIdea(relatedIdeaId);
                model.CategoryIds = relatedIdea.CategoryIds;
                model.MunicipalityId = relatedIdea.MunicipalityId;
                var relatedIdeas = IdeaService.GetRelatedIdeas(relatedIdeaId);
                relatedIdeas.Add(new RelatedIdeaListItem()
                {
                    Name = relatedIdea.Subject,
                    ObjectId = relatedIdeaId
                });

                model.RelatedIdeas = relatedIdeas;
                model.Problems = IdeaService.GetIdeaProblems(relatedIdeaId);

                if (CurrentUser.IsUserInOrganization(relatedIdea.OrganizationId))
                {
                    model.OrganizationId = relatedIdea.OrganizationId;
                    model.Visibility = relatedIdea.Visibility;
                }
            }

            return model;
        }

        public VotingCreateEditModel FillCreateEditModel(VotingCreateEditModel model)
        {
            model.Categories = categoryService.GetCategories().ToSelectList();
            model.Ideas = GetMyIdeas(null, model.OrganizationId);
            model.Municipalities = addressService.GetMunicipalities(1);
            model.Organizations = OrganizationService.GetUserOrganizations();
            model.IsMailSendable = UserService.IsMailSendable();
            if (model.Urls == null)
            {
                model.Urls = new List<UrlEditModel>();
            }
            return model;
        }

        public string Insert(VotingCreateEditModel model)
        {
            if (WorkContext.CategoryIds != null)
            {
                model.CategoryIds = WorkContext.CategoryIds;
            }

            var issue = new Issue
                            {
                                CategoryIds =
                                    model.CategoryIds,
                                RegistrationDate = DateTime.Now,
                                ModificationDate = DateTime.Now,
                                UserObjectId = CurrentUser.Id,
                                UserFullName = CurrentUser.FullName,
                                OrganizationId = !string.IsNullOrEmpty(model.OrganizationId) ? model.OrganizationId : null,
                                AdditionalInfoRequiredForVoting = model.AdditionalInfoRequiredForVoting,
                                AllowSummaryWiki = model.AllowSummaryWiki,
                                AllowNeutralVotes = model.AllowNeutralVotes,
                                Visibility = model.Visibility
                            };

            //SQL DB insert goes first to maximize transactioness.
            var efIssue = new Data.EF.Voting.Issue()
                              {
                                  RegistrationDate = DateTime.Now,
                                  ModificationDate = DateTime.Now,
                                  UserObjectId = CurrentUser.Id,
                                  OrganizationId = model.OrganizationId,
                                  Deadline = model.Deadline,
                                  IsPrivateToOrganization = model.IsPrivateToOrganization,
                                  MunicipalityId = model.MunicipalityId,
                                  Subject = model.Subject.RemoveNewLines().GetSafeHtml(),
                                  Summary = model.Summary.RemoveNewLines().Sanitize(),
                                  ObjectId = issue.Id
                              };

            using (var votingSession = votingSessionFactory.CreateContext(true))
            {
                votingSession.Issues.Add(efIssue);
            }

            issue.DbId = efIssue.Id;

            ChangeRelatedIdeaStates(model.RelatedIdeas.Where(m => m.ChangeState).Select(i => i.ObjectId).ToList());

            issue.InjectFrom<UniversalInjection>(model);
            issue.OrganizationName = OrganizationService.GetOrganizationName(model.OrganizationId);

            if (WorkContext.Municipality != null)
            {
                issue.MunicipalityId = WorkContext.Municipality.Id;
            }
            else if (issue.MunicipalityId == 0)
            {
                issue.MunicipalityId = null;
            }
            if (!string.IsNullOrEmpty(issue.DocumentUrl) && !issue.DocumentUrl.StartsWith("http"))
            {
                issue.DocumentUrl = "http://" + issue.DocumentUrl;
            }
            if (model.DeadlineTime.HasValue)
            {
                issue.Deadline += model.DeadlineTime.Value;
            }
            issue.SummaryWiki.Versions.Add(new WikiTextVersion
                                               {
                                                   CreatedOn = DateTime.Now,
                                                   CreatorFullName = CurrentUser.FullName,
                                                   CreatorObjectId = CurrentUser.Id,
                                                   Text = model.Summary.RemoveNewLines().Sanitize()
                                               });
            model.Urls.BindUrls(issue.Urls);
            issue.ShortLink = model.ShortLink ?? GetShortLink(issue);

            using (var noSqlSession = noSqlSessionFactory())
            {
                noSqlSession.Add(issue);
            }

            using (var votingSession = votingSessionFactory.CreateContext(true))
            {
                foreach (var problemId in model.Problems.Select(p => p.Id).Distinct())
                {
                    var p = new Data.EF.Voting.ProblemIssue()
                    {
                        IssueId = issue.Id.ToString(),
                        ProblemId = problemId,
                        UserObjectId = CurrentUser.Id
                    };
                    votingSession.ProblemIssues.Add(p);
                }

                foreach (var idea in model.RelatedIdeas.Where(i => !i.IsDeleted).Distinct())
                {
                    var ii = new IdeaIssue()
                                 {
                                     IdeaObjectId = idea.ObjectId,
                                     IssueObjectId = issue.Id,
                                     ChangeState = idea.ChangeState
                                 };
                    votingSession.IdeaIssues.Add(ii);
                }
            }

            ActionService.Subscribe(issue.Id, CurrentUser.DbId.Value, EntryTypes.Issue);

            var command = new IssueCommand
                              {
                                  ActionType = ActionTypes.IssueCreated,
                                  UserId = CurrentUser.Id,
                                  ObjectId = issue.Id.ToString(),
                                  Link =
                                      Url.ActionAbsolute("Details", "Voting",
                                                         new { id = issue.Id, subject = issue.Subject.ToSeoUrl() }),
                                  IsPrivate = issue.IsPrivateToOrganization,
                                  SendMail = model.SendMail
                              };

            bus.Send(command);

            return issue.Id;
        }

        private bool CanCurrentUserDelete(Issue issue)
        {
            return CurrentUser.Role == UserRoles.Admin ||
                   (issue.UserObjectId == CurrentUser.Id &&
                    (issue.SupportingVotesCount + issue.NonSupportingVotesCount) < 2);
        }

        public bool Delete(string id)
        {
            var issue = GetIssue(id);

            if (!CanCurrentUserDelete(issue))
            {
                return false;
            }

            using (var votingSession = votingSessionFactory.CreateContext())
            {
                var dbIssue = votingSession.Issues.SingleOrDefault(i => i.ObjectId == id);
                if (dbIssue != null)
                {
                    votingSession.IssueCategories.Delete(i => i.IssueId == dbIssue.Id);
                    foreach (var comment in votingSession.IssueComments.Where(i => i.IssueId == dbIssue.ObjectId).Select(c => c.Comment).ToList())
                    {
                        foreach (var cComment in comment.Comments1.ToList())
                        {
                            votingSession.Comments.Remove(cComment);
                        }

                        votingSession.Comments.Remove(comment);
                    }

                    votingSession.IssueComments.Delete(i => i.IssueId == dbIssue.ObjectId);
                    votingSession.IssueVersions.Delete(i => i.IssueId == dbIssue.Id);
                    votingSession.IdeaIssues.Delete(i => i.IssueObjectId == dbIssue.ObjectId);
                    votingSession.ProblemIssues.Delete(i => i.IssueId == dbIssue.ObjectId);
                    votingSession.Votes.Delete(i => i.IssueId == dbIssue.Id);
                    votingSession.Issues.Remove(dbIssue);
                    votingSession.SaveChanges();
                }
            }

            using (var actionSession = actionSessionFactory.CreateContext())
            {
                actionSession.Actions.Delete(a => a.ObjectId == id);
                actionSession.SaveChanges();
            }

            using (var mongoSession = noSqlSessionFactory())
            {
                mongoSession.Delete<Data.MongoDB.Issue>(i => i.Id == id);
            }

            return true;
        }

        private string GetShortLink(Issue issue)
        {
            return shortLinkService.GetShortLink(issue.ShortLink ?? issue.Subject,
                                                 GetDetailsUrl(issue));
        }

        private string GetDetailsUrl(Issue issue)
        {
            return Url.Action("Details", "Voting", new { id = issue.Id, subject = issue.Subject.ToSeoUrl() });
        }

        public void UpdateShortLinks()
        {
            foreach (var issue in GetAllIssues())
            {
                issue.ShortLink = GetShortLink(issue);
                UpdateIssue(issue);
            }
        }

        public VotingViewModel GetIssueView(MongoObjectId id)
        {
            var view = GetIssueViewInternal(id);

            SetPageViewed(view);

            return view;
        }

        public IEnumerable<VotingViewModel.WikiVersionModel> GetVersions(MongoObjectId issueId)
        {
            object versions;
            if (cache.GetItem(GetCacheKey(issueId.ToString()), out versions))
            {
                return versions as IEnumerable<VotingViewModel.WikiVersionModel>;
            }

            return GetIssueViewInternal(issueId).Versions;
        }

        public VotingViewModel AddNewVersion(MongoObjectId id, string text, UserInfo userInfo)
        {
            var version = new WikiTextVersion
                                               {
                                                   CreatedOn = DateTime.Now,
                                                   CreatorFullName = userInfo.FullName,
                                                   CreatorObjectId = userInfo.Id,
                                                   Text = text.RemoveNewLines().Sanitize()
                                               };
            using (var noSqlSession = noSqlSessionFactory())
            {
                noSqlSession.UpdateById<Issue>(id, Update.PushWrapped("SummaryWiki.Versions", version));
            }

            cache.InvalidateItem(GetCacheKey(id.ToString()));

            var issue = GetIssue(id);
            bus.Send(new IssueCommand
                            {
                                ActionType = ActionTypes.SummaryVersionAdded,
                                UserId = userInfo.Id,
                                ObjectId = id.ToString(),
                                Text = text.RemoveNewLines().Sanitize(),
                                IsPrivate = issue.IsPrivateToOrganization,
                                Link = Url.Action("Details", "Voting", new { id = issue.Id })
                            });

            return GetViewModelFromIssue(issue);
        }

        protected override ICommentable GetEntity(MongoObjectId id)
        {
            return GetIssue(id);
        }

        protected override ActionTypes GetAddNewCommentActionType()
        {
            return ActionTypes.Commented;
        }

        protected override ActionTypes GetLikeCommentActionType()
        {
            return ActionTypes.LikedComment;
        }

        protected override void SendCommentCommand(ICommentable entity, ActionTypes actionType, CommentView comment)
        {
            var issue = entity as Data.MongoDB.Issue;
            var relatedUserId = comment.AuthorObjectId;
            if (!string.IsNullOrEmpty(comment.ParentId) && actionType == ActionTypes.CommentCommented)
            {
                relatedUserId =
                    entity.Comments.Where(c => c.Id == comment.ParentId).Select(c => c.UserObjectId).SingleOrDefault();
            }
            bus.Send(new IssueCommand
                         {
                             ActionType = actionType,
                             ObjectId = comment.EntryId,
                             Text = comment.CommentText,
                             RelatedUserId = relatedUserId,
                             UserId = CurrentUser.Id,
                             CommentId = !string.IsNullOrEmpty(comment.ParentId) ? comment.ParentId : comment.Id,
                             CommentCommentId = !string.IsNullOrEmpty(comment.ParentId) ? comment.Id : null,
                             Link = Url.Action("Details", "Voting", new { id = comment.EntryId }),
                             IsPrivate = issue.IsPrivateToOrganization
                         });
        }

        public override CommentView AddNewComment(MongoObjectId id, string text, EmbedModel embed, ForAgainst forAgainst = ForAgainst.Neutral, MongoObjectId versionId = null)
        {
            var entity = GetIssue(id);
            VotingStatisticsViewModel stats = null;
            if (CurrentUser.IsUnique && GetVote(entity.DbId, CurrentUser.PersonCode) == null)
            {
                try
                {
                    stats = Vote(entity, forAgainst);
                }
                catch (Exception ex)
                {
                    logger.Information("Exception occured when auto voting after comment", ex);
                }
            }

            var model = base.AddNewComment(id, text, embed, forAgainst, versionId);
            model.VotingStatistics = stats;

            return model;
        }

        public VersionViewModel GetVersion(MongoObjectId id, MongoObjectId versionId)
        {
            return (from v in GetVersions(id)
                    where v.ObjectId == versionId
                    select new VersionViewModel()
                    {
                        Text = v.VersionText
                    }).Single();
        }

        public VotingStatisticsViewModel Vote(Issue issue, ForAgainst posOrNeg)
        {
            if (issue.IsClosed)
            {
                return null;
            }

            if (!CurrentUser.IsUnique)
            {
                if (!CurrentUser.IsAuthenticated)
                {
                    throw new UnauthorizedAccessException();
                }
            }
            else
            {

                if (CurrentUser.IsViispConfirmed)
                {
                    throw new UnauthorizedAccessException();
                }

                if (issue.AdditionalInfoRequiredForVoting && CurrentUser.AdditionalInfo.AdditionalInfoRequired)
                {
                    var info = UserService.GetAdditionalVotingInfo(CurrentUser.PersonCode);
                    throw new AdditionalUniqueInfoRequiredException(info);
                }
            }

            if (CurrentUser.IsUnique)
            {
                using (var votingSession = votingSessionFactory.CreateContext())
                {
                    var existingVote = GetVote(issue.DbId, CurrentUser.PersonCode);
                    if (existingVote == null)
                    {
                        existingVote = new Data.EF.Voting.Vote
                            {
                                IssueId = issue.DbId,
                                PersonCode = CurrentUser.PersonCode
                            };

                        votingSession.Votes.Add(existingVote);
                    }

                    existingVote.ForAgainst = (short)posOrNeg;
                    existingVote.Date = DateTime.Now;
                    existingVote.FirstName = CurrentUser.FirstName;
                    existingVote.LastName = CurrentUser.LastName;
                    existingVote.AddressLine = CurrentUser.AdditionalInfo.AddressLine;
                    existingVote.DocumentNo = CurrentUser.AdditionalInfo.DocumentNo;
                    existingVote.CityId = CurrentUser.AdditionalInfo.CityId;
                    existingVote.Source = CurrentUser.AuthenticationSource;
                    existingVote.IsPublic = CurrentUser.VotesArePublic;
                    existingVote.UserObjectId = CurrentUser.Id;
                    existingVote.Sign(issue.Subject, issue.Summary);

                    votingSession.SaveChanges();

                    if (CurrentUser.IsAuthenticated)
                    {
                        var vote = issue.AdditionalVotes.SingleOrDefault(v => v.Id == CurrentUser.Id);
                        if (vote != null)
                        {
                            issue.AdditionalVotes.Remove(vote);
                            UpdateIssue(issue, false);
                        }
                    }

                    RecountVotes(issue);
                }
            }
            else
            {
                var vote = issue.AdditionalVotes.SingleOrDefault(v => v.Id == CurrentUser.Id);
                if (vote == null)
                {
                    vote = new SupportingUser()
                        {
                            Id = CurrentUser.Id,
                            FullName = CurrentUser.FullName,
                            IsPublic = CurrentUser.VotesArePublic,
                            ForAgainst = posOrNeg
                        };
                    issue.AdditionalVotes.Add(vote);
                }

                vote.ForAgainst = posOrNeg;
                UpdateIssue(issue, false);
            }

            SubscribeModel subscribe = null;
            if (CurrentUser.IsAuthenticated)
            {
                subscribe = ActionService.Subscribe(issue.Id, CurrentUser.DbId.Value, EntryTypes.Issue);
            }

            return new VotingStatisticsViewModel()
                       {
                           Id = issue.Id,
                           SupportPercentage = issue.GetSupportPercentage(),
                           Vote = posOrNeg,
                           AdditionalVote = posOrNeg,
                           VotesCount = issue.SupportingVotesCount + issue.NonSupportingVotesCount + issue.NeutralVotesCount,
                           IsVotingFinished = issue.IsClosed,
                           TimeLeft = GlobalizedSentences.GetTimeLeftString(issue.Deadline),
                           Deadline = issue.Deadline,
                           SupportingVotesCount = issue.SupportingVotesCount,
                           NonSupportingVotesCount = issue.NonSupportingVotesCount,
                           NeutralVotesCount = issue.NeutralVotesCount,
                           Subscribe = subscribe,
                           SupportingAdditionalVotesCount = issue.AdditionalVotes.Count(v => v.ForAgainst == ForAgainst.For),
                           NonSupportingAdditionalVotesCount = issue.AdditionalVotes.Count(v => v.ForAgainst == ForAgainst.Against),
                           NeutralAdditionalVotesCount = issue.AdditionalVotes.Count(v => v.ForAgainst == ForAgainst.Neutral),
                           AllowNeutralVotes = issue.AllowNeutralVotes
                       };
        }

        private void RecountVotes(Data.MongoDB.Issue issue)
        {
            using (var votingSession = votingSessionFactory.CreateContext())
            {
                var votes =
                    votingSession.Votes.Where(v => v.IssueId == issue.DbId).Select(
                        v => v.ForAgainst).ToList();
                issue.SupportingVotesCount = votes.Count(v => v == (short)ForAgainst.For);
                issue.NonSupportingVotesCount = votes.Count(v => v == (short)ForAgainst.Against);
                issue.NeutralVotesCount = votes.Count(v => v == (short)ForAgainst.Neutral);
                UpdateIssue(issue);
            }
        }

        public VotingStatisticsViewModel CancelVote(Issue issue)
        {
            if (issue.IsClosed)
            {
                return null;
            }

            if (CurrentUser.IsUnique)
            {
                using (var votingSession = votingSessionFactory.CreateContext(true))
                {
                    var existingVote = GetVote(issue.DbId, CurrentUser.PersonCode);

                    votingSession.Votes.Remove(existingVote);
                }

                RecountVotes(issue);
            }
            else if (CurrentUser.IsAuthenticated)
            {
                var vote = issue.AdditionalVotes.SingleOrDefault(v => v.Id == CurrentUser.Id);
                if (vote != null)
                {
                    issue.AdditionalVotes.Remove(vote);
                    UpdateIssue(issue, false);
                }
            }

            return new VotingStatisticsViewModel()
                       {
                           Id = issue.Id,
                           SupportPercentage = issue.GetSupportPercentage(),
                           Vote = null,
                           VotesCount = issue.SupportingVotesCount + issue.NonSupportingVotesCount,
                           IsVotingFinished = issue.IsClosed,
                           TimeLeft = GlobalizedSentences.GetTimeLeftString(issue.Deadline),
                           Deadline = issue.Deadline,
                           SupportingVotesCount = issue.SupportingVotesCount,
                           NonSupportingVotesCount = issue.NonSupportingVotesCount,
                           SupportingAdditionalVotesCount = issue.AdditionalVotes.Count(v => v.ForAgainst == ForAgainst.For),
                           NonSupportingAdditionalVotesCount = issue.AdditionalVotes.Count(v => v.ForAgainst == ForAgainst.Against),
                           AllowNeutralVotes = issue.AllowNeutralVotes
                       };
        }

        public VotingStatisticsViewModel Vote(MongoObjectId id, ForAgainst posOrNeg)
        {
            var issue = GetIssue(id);

            var model = Vote(issue, posOrNeg);

            if (CurrentUser.IsAuthenticated)
            {
                bus.Send(new IssueCommand
                             {
                                 ActionType = ActionTypes.Voted,
                                 UserId = CurrentUser.Id,
                                 ObjectId = issue.Id.ToString(),
                                 IsPrivate = issue.IsPrivateToOrganization || !CurrentUser.VotesArePublic,
                                 Link = Url.Action("Details", "Voting", new { id = issue.Id })
                             });
            }

            return model;
        }

        public VotingStatisticsViewModel CancelVote(MongoObjectId id)
        {
            var issue = GetIssue(id);

            var model = CancelVote(issue);

            bus.Send(new VoteCancelledCommand
            {
                UserId = CurrentUser.Id,
                ObjectId = issue.Id.ToString(),
            });

            return model;
        }

        public void LikeCategories(IList<CategorySelectModel> categories)
        {
            var cats = categories.Where(c => c.IsSelected).Select(c => (int)c.CategoryId).ToList();

            categoryService.SaveMyCategories(cats);
        }

        public void LikeCategories(IList<int> selectedCategoryIds)
        {
            categoryService.SaveMyCategories(selectedCategoryIds);
        }

        public IList<CategorySelectModel> GetMyCategories()
        {
            return categoryService.GetMyCategoriesModel();
        }

        public IEnumerable<SelectListItem> GetIdeas()
        {
            using (var noSqlSession = noSqlSessionFactory())
            {

                return noSqlSession.Find<Idea>(GetIdeaQuery())
                    .SetSortOrder(SortBy.Ascending("Subject"))
                    .SetFields("_id", "Subject", "IsDraft").ToList()
                    .Where(q => q.IsDraft == false).Select(i => new TextValue()
                                                {
                                                    Text = i.Subject,
                                                    Value = i.Id
                                                }).ToList().ToSelectList();
            }
        }

        public IEnumerable<SelectListItem> GetMyIdeas(List<int> categoryIds, string organizationId)
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
                    .Where(q => q.IsDraft == false && (!q.IsPrivateToOrganization || q.OrganizationId == organizationId))
                    .Select(i => new TextValue()
                {
                    Text = i.Subject,
                    Value = i.Id
                }).ToList().ToSelectList();
            }
        }

        private IMongoQuery GetIdeaQuery()
        {
            var orgs = CurrentUser.OrganizationIds;
            List<BsonValue> orgsBson = new List<BsonValue>();
            foreach (var org in orgs)
            {
                orgsBson.Add(BsonValue.Create(org));
            }

            var q = Query.And
                    (Query.NotIn("State", new[] { BsonValue.Create(IdeaStates.Closed), BsonValue.Create(IdeaStates.Realized) }),
                     Query.Or(Query.NotExists("IsPrivateToOrganization"), Query.EQ("IsPrivateToOrganization", false), Query.In("OrganizationId", orgsBson.ToArray())));
            return q;
        }

        public string GetIdeaSubject(string id)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                return noSqlSession.Find<Idea>(Query.EQ("_id", BsonObjectId.Create(id))).SetFields("Subject").Single().Subject;
            }
        }

        private static string GetCacheKey(string issueObjectId)
        {
            return string.Format("Issue_{0}_Version_Cache", issueObjectId);
        }

        private bool CanCurrentUserEdit(Issue issue)
        {
            return CurrentUser.IsAuthenticated &&
                (issue.SummaryWiki.Versions.Any(v => v.CreatorObjectId == CurrentUser.Id) ||
                CurrentUser.Role == UserRoles.Admin ||
                CurrentUser.ExpertCategoryIds.Intersect(issue.CategoryIds).Any() ||
                CurrentUser.Organizations.Where(o => o.OrganizationId == issue.OrganizationId).Select(o => o.Permission).SingleOrDefault() != UserRoles.Basic);
        }

        private VotingViewModel GetViewModelFromIssue(Issue issue)
        {
            var view = new VotingViewModel();
            view.Id = issue.Id;
            view.Subject = issue.Subject;
            view.Deadline = issue.Deadline;
            view.DocumentUrl = issue.DocumentUrl;
            view.OfficialVote = issue.OfficialVote;
            view.OfficialVotingDescription = issue.OfficialVotingDescription;
            view.ShortLink = issue.ShortLink;
            view.Summary = issue.Summary;
            view.Categories = issue.CategoryIds.Select(cId => categoryService.GetCategoryName(cId)).ToList();
            view.Municipality = addressService.GetMunicipality(issue.MunicipalityId);
            view.Vote = GetVoteForAgainst(issue);
            view.AdditionalVote = GetAdditionalVoteForAgainst(issue);

            view.IsVotingFinished = issue.IsClosed;
            view.VotesCount = issue.SupportingVotesCount + issue.NonSupportingVotesCount + issue.NeutralVotesCount;
            view.TimeLeft = GlobalizedSentences.GetTimeLeftString(view.Deadline);
            view.IsEditable = CanCurrentUserEdit(issue);
            view.IsDeletable = CanCurrentUserDelete(issue);
            view.Date = issue.RegistrationDate;
            view.ShortLink = issue.ShortLink ?? string.Empty;
            view.SupportingAdditionalVotesCount = issue.AdditionalVotes.Count(v => v.ForAgainst == ForAgainst.For);
            view.NonSupportingAdditionalVotesCount = issue.AdditionalVotes.Count(v => v.ForAgainst == ForAgainst.Against);
            view.NeutralAdditionalVotesCount = issue.AdditionalVotes.Count(v => v.ForAgainst == ForAgainst.Neutral);
            view.AllowSummaryWiki = issue.AllowSummaryWiki;
            view.AllowNeutralVotes = issue.AllowNeutralVotes;
            view.Visibility = issue.Visibility;
            view.OrganizationId = issue.OrganizationId;
            view.OrganizationName = issue.OrganizationName;
            view.RegisteredBy = issue.RegisteredBy;
            view.SupportingVotesCount = issue.SupportingVotesCount;
            view.NonSupportingVotesCount = issue.NonSupportingVotesCount;
            view.NeutralVotesCount = issue.NeutralVotesCount;

            view.SupportPercentage = issue.GetSupportPercentage();

            foreach (var version in issue.SummaryWiki.Versions.OrderBy(v => v.CreatedOn))
            {
                view.Versions.Add(new VotingViewModel.WikiVersionModel
                {
                    ObjectId = version.Id,
                    UserObjectId = version.CreatorObjectId,
                    UserFullName = version.CreatorFullName,
                    VersionName = version.CreatedOn.ToString(),
                    VersionText = version.Text,
                });
            }

            view.UserFullName = issue.UserFullName;
            view.UserObjectId = issue.UserObjectId;

            view.PositiveComments = commentService.GetCommentsMostSupported(issue, 0, ForAgainst.For);
            view.NegativeComments = commentService.GetCommentsMostSupported(issue, 0, ForAgainst.Against);
            view.PositiveCommentsCount = issue.Comments.Count(c => c.PositiveOrNegative == ForAgainst.For);
            view.NegativeCommentsCount = issue.Comments.Count(c => c.PositiveOrNegative == ForAgainst.Against);

            view.RelatedIdeas = GetRelatedIdeas(issue.Id);
            view.Problems =
                GetIssueProblems(issue.Id).Select(p => new SimpleListModel() { Subject = p.Text, Id = p.Id }).ToList();
            view.OrganizationId = issue.OrganizationId;
            view.OrganizationName = issue.OrganizationName;
            view.IsMailSendable = IsMailSendable(issue, view.IsEditable);

            view.Urls = issue.Urls.Select(w => new UrlViewModel
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

            if (CurrentUser.IsAuthenticated)
            {
                view.Subscribe = ActionService.IsSubscribed(issue.Id, CurrentUser.DbId.Value, EntryTypes.Issue);
            }

            return view;
        }

        public bool SendNotification(string issueId)
        {
            if (!UserService.CanSendMail())
            {
                return false;
            }

            var issue = GetIssue(issueId);
            if (!issue.IsMailSent)
            {
                UserService.SetMailSendDate();
                bus.Send(new SendObjectCreatedNotificationCommand()
                {
                    ObjectId = issueId,
                    Link = Url.ActionAbsolute("Details", "Voting",
                                              new { id = issue.Id, subject = issue.Subject.ToSeoUrl() }),
                    Type = NotificationTypes.IssueCreated,
                    UserObjectId = CurrentUser.Id
                });
                return true;
            }

            return false;
        }

        private bool IsMailSendable(Issue issue, bool isEditable)
        {
            return !issue.IsMailSent && isEditable && (UserService.IsMailSendable() || issue.IsPrivateToOrganization);
        }

        private List<ProblemIndexItemModel> GetIssueProblems(string issueId)
        {
            using (var session = votingSessionFactory.CreateContext())
            {
                return (from pi in session.ProblemIssues
                        where pi.IssueId == issueId
                        select new ProblemIndexItemModel()
                        {
                            Id = pi.ProblemId,
                            Text = pi.Problem.Text
                        }).ToList();
            }
        }

        private ForAgainst? GetVoteForAgainst(List<Data.EF.Voting.Vote> votes, int issueDbId)
        {
            if (!CurrentUser.IsUnique)
            {
                return null;
            }
            var vote = votes.SingleOrDefault(v => v.IssueId == issueDbId);
            if (vote == null)
            {
                return null;
            }

            return (ForAgainst)Convert.ToInt16(vote.ForAgainst);
        }

        private ForAgainst? GetAdditionalVoteForAgainst(Issue issue)
        {
            if (!CurrentUser.IsAuthenticated)
            {
                return null;
            }

            if (CurrentUser.IsUnique)
            {
                return null;
            }

            var vote = issue.AdditionalVotes.SingleOrDefault(v => v.Id == CurrentUser.Id);
            if (vote == null)
            {
                return null;
            }

            return vote.ForAgainst;
        }

        private ForAgainst? GetVoteForAgainst(Issue issue)
        {
            if (!CurrentUser.IsUnique)
            {
                return null;
            }

            var vote = GetVote(issue.DbId, CurrentUser.PersonCode);
            if (vote == null)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(issue.OrganizationId) && issue.IsPrivateToOrganization && !issue.IsClosed)
            {
                if (!vote.ValidateSignature(issue.Subject, issue.Summary))
                {
                    return null;
                }
            }

            return (ForAgainst)Convert.ToInt16(vote.ForAgainst);
        }

        private ForAgainst? GetVoteForAgainst(int issueDbId, string personCode)
        {
            var vote = GetVote(issueDbId, personCode);
            if (vote == null)
            {
                return null;
            }

            return (ForAgainst)vote.ForAgainst;
        }

        private Data.EF.Voting.Vote GetVote(int issueDbId, string personCode)
        {
            using (var votingSession = votingSessionFactory.CreateContext())
            {
                return votingSession.Votes.SingleOrDefault(
                v => v.IssueId == issueDbId && v.PersonCode == personCode);
            }
        }

        private List<Data.EF.Voting.Vote> GetUserVotes()
        {
            return GetUserVotes(CurrentUser.DbId, CurrentUser.PersonCode);
        }

        private List<Data.EF.Voting.Vote> GetUserVotes(int? userDbId, string personCode)
        {
            using (var votingSession = votingSessionFactory.CreateContext())
            {
                return votingSession.Votes.Where(
                        v => v.PersonCode == personCode).ToList();
            }
        }

        private Issue GetIssue(MongoObjectId issueId)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                return noSqlSession.GetById<Issue>(issueId);
            }
        }

        private Comment GetComment(Issue issue, MongoObjectId commentId)
        {
            return issue.Comments.Where(c => c.Id == commentId).SingleOrDefault();
        }

        private void UpdateIssue(Issue issue, bool modifyDate = true)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                if (modifyDate)
                {
                    issue.ModificationDate = DateTime.Now;
                }

                noSqlSession.Update(issue);
            }
        }

        private void CacheVersions(VotingViewModel issue)
        {
            cache.PutItem(GetCacheKey(issue.Id), issue.Versions, new string[0], new TimeSpan(0, 10, 0), DateTime.MaxValue);
        }

        private IQueryable<Issue> GetMyCategoriesQuery()
        {
            var myCategoryIds = categoryService.GetMyCategoryIds();
            using (var session = noSqlSessionFactory())
            {
                List<BsonValue> arr = new List<BsonValue>();
                foreach (var id in myCategoryIds)
                {
                    arr.Add(BsonValue.Create(id));
                }
                return session.GetAllIn<Issue>("CategoryIds", arr.ToArray());
            }
        }

        private IQueryable<Issue> GetSelectedCategoriesQuery(IEnumerable<int> selectedCategories)
        {
            using (var session = noSqlSessionFactory())
            {
                List<BsonValue> arr = new List<BsonValue>();
                foreach (var item in selectedCategories)
                {
                    arr.Add(BsonValue.Create(item));
                }
                return session.GetAllIn<Issue>("CategoryIds", arr.ToArray());
            }
        }

        private IQueryable<Issue> GetSelectedCategoriesInversedQuery(IEnumerable<SelectListItem> selectedCategories)
        {
            using (var session = noSqlSessionFactory())
            {
                List<BsonValue> arr = new List<BsonValue>();
                foreach (var item in selectedCategories)
                {
                    arr.Add(BsonValue.Create(Convert.ToInt32(item.Value)));
                }
                return session.GetAllNotIn<Issue>("CategoryIds", arr.ToArray());
            }
        }

        private IQueryable<Issue> GetNotMyCategoriesQuery()
        {
            var myCategoryIds = categoryService.GetMyCategoryIds();
            using (var session = noSqlSessionFactory())
            {
                List<BsonValue> arr = new List<BsonValue>();
                foreach (var id in myCategoryIds)
                {
                    arr.Add(BsonValue.Create(id));
                }

                return session.GetAllNotIn<Issue>("CategoryIds", arr.ToArray());
            }
        }

        private void EnsureIssueIndices()
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                noSqlSession.CreateIndex<Issue>("IX_Idea_Deadline", false, "Deadline");
            }
        }

        private VotingViewModel GetIssueViewInternal(MongoObjectId id)
        {
            var issue = GetIssue(id);
            if (issue == null)
            {
                throw new ObjectNotFoundException();
            }

            return GetIssueViewInternal(issue);
        }

        private VotingViewModel GetIssueViewInternal(Issue issue)
        {
            var view = GetViewModelFromIssue(issue);

            CacheVersions(view);

            return view;
        }

        private void SetPageViewed(VotingViewModel issue)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                noSqlSession.UpdateById<Issue>(issue.Id, Update.Inc("ViewsCount", 1));
            }

            if (CurrentUser.IsAuthenticated)
            {
                bus.Send(new IssueCommand
                             {
                                 ActionType = ActionTypes.IssueViewed,
                                 UserId = CurrentUser.Id,
                                 ObjectId = issue.Id,
                                 IsPrivate = issue.IsPrivateToOrganization,
                                 Link = Url.Action("Details", "Voting", new { id = issue.Id })
                             });
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

        private void ChangeRelatedIdeaStates(List<string> ids, IdeaStates state = IdeaStates.Voting)
        {
            foreach (var id in ids)
            {
                using (var noSqlSession = noSqlSessionFactory())
                {
                    var idea = noSqlSession.FindByIdAndModify<Idea>(id, Update.Set("State", state));
                    bus.Send(new IdeaCommand()
                    {
                        ActionType = ActionTypes.IdeaStateChanged,
                        ObjectId = id,
                        UserId = CurrentUser.Id,
                        Text = IdeaStatesResource.ResourceManager.GetString(state.ToString()),
                        IsPrivate = idea.IsPrivateToOrganization,
                        Link = Url.Action("Details", "Idea", new { id = id })
                    });
                }
            }
        }

        public VotingViewModel OfficialVote(string id, ForAgainst forAgainst, string description)
        {
            var issue = GetIssue(id);
            issue.OfficialVote = forAgainst;
            issue.OfficialVotingDescription = description.RemoveNewLines().Sanitize();
            UpdateIssue(issue);

            if (forAgainst != ForAgainst.Neutral)
            {
                bus.Send(new IssueCommand()
                             {
                                 ActionType = ActionTypes.IssueOfficialyVoted,
                                 ObjectId = issue.Id,
                                 UserId = CurrentUser.Id,
                                 Text = (forAgainst == ForAgainst.For ? "Už" : "Prieš") + ": " + issue.OfficialVotingDescription,
                                 IsPrivate = issue.IsPrivateToOrganization,
                                 Link = Url.Action("Details", "Voting", new { id = issue.Id })
                             });
            }

            using (var votingSession = votingSessionFactory.CreateContext(true))
            {
                var relatedIdeas = votingSession.IdeaIssues.Where(i => i.IssueObjectId == id && i.ChangeState).Select(cId => cId.IdeaObjectId).ToList();
                if (relatedIdeas.Any())
                {
                    if (issue.OfficialVote == ForAgainst.For)
                    {
                        ChangeRelatedIdeaStates(relatedIdeas, IdeaStates.Realized);
                    }
                    else if (issue.OfficialVote == ForAgainst.Against)
                    {
                        ChangeRelatedIdeaStates(relatedIdeas, IdeaStates.Rejected);
                    }
                    else if (issue.OfficialVote == ForAgainst.Neutral)
                    {
                        ChangeRelatedIdeaStates(relatedIdeas, IdeaStates.Voting);
                    }
                }
            }

            return GetViewModelFromIssue(issue);
        }

        private List<RelatedIdeaListItem> GetRelatedIdeas(string issueId)
        {
            using (var votingSession = votingSessionFactory.CreateContext())
            {
                var relatedIdeas = votingSession.IdeaIssues.Where(i => i.IssueObjectId == issueId).Select(cId => new RelatedIdeaListItem
                {
                    ObjectId = cId.IdeaObjectId,
                    ChangeState = cId.ChangeState
                }).ToList();

                return IdeaService.GetRelatedIdeas(relatedIdeas);
            }
        }

        private List<int> GetMyMunicipalities()
        {
            if (CurrentUser.Municipalities == null)
            {
                if (!CurrentUser.IsAuthenticated)
                {
                    return new List<int>();
                }

                var muns = addressService.GetUserMunicipalities(CurrentUser.Id);
                CurrentUser.Municipalities = muns;
            }

            return CurrentUser.Municipalities;
        }

        public bool UpdateIssueDb()
        {
            //using (var session = noSqlSessionFactory())
            //{
            //    foreach (var issue in session.GetAll<Data.MongoDB.Issue>())
            //    {
            //        UpdateDbIssue(issue);
            //    }
            //}

            using (var session = noSqlSessionFactory())
            {
                foreach (var issue in session.GetAll<Data.MongoDB.Issue>())
                {
                    issue.Visibility = issue._isItPrivate ? ObjectVisibility.Private : ObjectVisibility.Public;
                    UpdateIssue(issue);
                }
            }

            return true;
        }

        public void UpdateDbIssue(string issueId)
        {
            UpdateDbIssue(GetIssue(issueId));
        }

        public void UpdateDbIssue(Data.MongoDB.Issue issue)
        {
            using (var votingSession = votingSessionFactory.CreateContext(true))
            {
                var dbIssue = votingSession.Issues.SingleOrDefault(i => i.Id == issue.DbId);
                if (dbIssue == null)
                {
                    dbIssue = new Data.EF.Voting.Issue()
                        {
                            Id = issue.DbId
                        };
                    votingSession.Issues.Add(dbIssue);
                }
                dbIssue.UserObjectId = issue.UserObjectId;
                dbIssue.RegistrationDate = issue.RegistrationDate.Truncate();
                dbIssue.ModificationDate = (issue.ModificationDate ?? issue.RegistrationDate).Truncate();
                dbIssue.OrganizationId = issue.OrganizationId;
                dbIssue.Deadline = issue.Deadline.HasValue ? issue.Deadline.Value.Truncate() : (DateTime?)null;
                dbIssue.IsPrivateToOrganization = issue.IsPrivateToOrganization;
                dbIssue.MunicipalityId = issue.MunicipalityId;
                dbIssue.Subject = issue.Subject;
                dbIssue.Summary = issue.Summary;
                dbIssue.ObjectId = issue.Id;
                dbIssue.OfficialVote = (short)issue.OfficialVote;
                dbIssue.OfficialVotingDescription = issue.OfficialVotingDescription;
                dbIssue.IsMailSent = issue.IsMailSent;

                //votingSession.IssueCategories.Delete(ic => ic.IssueId == issue.DbId);
                //foreach (var cat in issue.CategoryIds)
                //{
                //    dbIssue.IssueCategories.Add(new IssueCategory()
                //        {
                //            CategoryId = cat
                //        });
                //}

                var dbCategories = votingSession.IssueCategories.Where(ic => ic.IssueId == issue.DbId).ToList();
                var deletedCategoryIds = dbCategories.Select(
                    ic => ic.CategoryId).Except(issue.CategoryIds.Select(ri => ri)).ToList();
                if (deletedCategoryIds.Any())
                {
                    votingSession.IssueCategories.Delete(
                        c => deletedCategoryIds.Contains(c.CategoryId) && c.IssueId == issue.DbId);
                }

                foreach (var categoryId in issue.CategoryIds)
                {
                    if (!dbCategories.Any(c => c.CategoryId == categoryId))
                    {
                        dbIssue.IssueCategories.Add(new IssueCategory()
                        {
                            CategoryId = categoryId
                        });
                    }
                }

                foreach (var version in issue.SummaryWiki.Versions)
                {
                    string versionId = version.Id.ToString();
                    var dbVersion =
                        votingSession.IssueVersions.SingleOrDefault(
                            v => v.IssueId == issue.DbId && v.VersionId == versionId);
                    if (dbVersion == null)
                    {
                        dbVersion = new Data.EF.Voting.IssueVersion()
                            {
                                VersionId = version.Id
                            };
                        dbIssue.IssueVersions.Add(dbVersion);
                    }
                    dbVersion.CreatedOn = version.CreatedOn.Truncate();
                    dbVersion.ModifiedOn = (dbVersion.ModifiedOn == DateTime.MinValue
                                               ? version.CreatedOn
                                               : dbVersion.ModifiedOn).Truncate();
                    dbVersion.UserObjectId = version.CreatorObjectId;
                    dbVersion.Text = version.Text;
                }
                foreach (var deletedVersion in votingSession.IssueVersions.Where(v => v.IssueId == issue.DbId))
                {
                    if (issue.SummaryWiki.Versions.All(v => v.Id.ToString() != deletedVersion.VersionId))
                    {
                        votingSession.IssueVersions.Remove(deletedVersion);
                    }
                }

                issue.Comments.RemoveAll(c => string.IsNullOrEmpty(c.Text));
                UpdateIssue(issue);

                votingSession.SaveChanges();

                commentService.UpdateComments(issue.Id, issue.Comments, EntryTypes.Issue);
            }
        }

        public List<SimpleListModel> GetIssuesList(IssueListViews view, IssueListSorts sort, IEnumerable<int> categories, string organizationId)
        {
            var list = GetIssuePage(0, int.MaxValue - 1, view, sort, categories, organizationId);
            if (!list.Items.List.Any())
            {
                list = GetIssuePage(0, int.MaxValue - 1, view, sort, null, organizationId);
            }

            return list.Items.List.Select(l => new SimpleListModel()
            {
                Id = l.Id,
                Subject = l.Subject
            }).ToList();
        }

        public List<SimpleListModel> GetResultsList(IssueListViews view, IssueListSorts sort, IEnumerable<int> categories, string organizationId)
        {
            var list = GetResultsPage(0, int.MaxValue - 1, view, sort, categories, organizationId);
            if (!list.Items.List.Any())
            {
                list = GetResultsPage(0, int.MaxValue - 1, view, sort, null, organizationId);
            }

            return list.Items.List.Select(l => new SimpleListModel()
            {
                Id = l.Id,
                Subject = l.Subject
            }).ToList();
        }

        public IList<LabelValue> GetMatchedIssues(string prefix)
        {
            using (var session = votingSessionFactory.CreateContext())
            {
                return (from i in session.Issues
                        where i.Subject.Contains(prefix) && (!i.IsPrivateToOrganization || CurrentUser.OrganizationIds.Contains(i.OrganizationId))
                        select new LabelValue { label = i.Subject, value = i.Subject, id = i.ObjectId }).Take(20).ToList();
            }
        }

        public IssueDocumentModel GetIssueDocumentModel(string id)
        {
            using (var session = votingSessionFactory.CreateContext())
            {
                var votes = session.Votes.Where(v => v.Issue.ObjectId == id).ToList();

                var model = (from issue in session.Issues
                    where issue.ObjectId == id
                    select new IssueDocumentModel()
                    {
                        Subject = issue.Subject,
                        Summary = issue.Summary,
                        CreatedByUserId = issue.UserObjectId,
                        OfficialVoteDesciprtion = issue.OfficialVotingDescription,
                        OrganizationId = issue.OrganizationId
                    }).SingleOrDefault();

                if (model.CreatedByUserId != CurrentUser.Id && CurrentUser.Role != UserRoles.Admin)
                {
                    throw new UnauthorizedAccessException("Only issue author can generate results document");
                }

                using (var actionSession = actionSessionFactory.CreateContext())
                {
                    var orgUserVotes =
                        actionSession.UserInterestingOrganizations.Where(u => u.OrganizationId == model.OrganizationId)
                            .Select(m => new {m.UserId, m.VoteCount}).ToList();



                    var votesCount = from v in votes
                        join o in orgUserVotes on v.UserObjectId equals UserService.GetUserObjectId(o.UserId)
                        select new {Vote = v, VoteCount = o.VoteCount};

                    foreach (var vote in votesCount)
                    {
                        var voteCount = vote.VoteCount;
                        if (voteCount == 0)
                        {
                            voteCount = 1;
                        }

                        var isValid = vote.Vote.ValidateSignature(model.Subject, model.Summary);
                        for (int i = 0; i < voteCount; i++)
                        {
                            model.Users.Add(new IssueDocumentUserModel()
                            {
                                UserId = vote.Vote.UserObjectId,
                                FirstName = vote.Vote.FirstName,
                                LastName = vote.Vote.LastName,
                                Date = vote.Vote.Date,
                                Vote = (ForAgainst) vote.Vote.ForAgainst,
                                Source = vote.Vote.Source,
                                IsValid = isValid
                            });
                        }
                    }

                    model.SupportingVotesCount = model.Users.Count(u => u.Vote == ForAgainst.For && u.IsValid);
                    model.NonSupportingVotesCount = model.Users.Count(u => u.Vote == ForAgainst.Against && u.IsValid);
                    model.NeutralVotesCount = model.Users.Count(u => u.Vote == ForAgainst.Neutral && u.IsValid);

                    model.SupportingUsersCount =
                        model.Users.Where(u => u.Vote == ForAgainst.For && u.IsValid)
                            .Select(u => u.UserId)
                            .Distinct()
                            .Count();
                    model.NonSupportingUsersCount =
                        model.Users.Where(u => u.Vote == ForAgainst.Against && u.IsValid)
                            .Select(u => u.UserId)
                            .Distinct()
                            .Count();
                    model.NeutralUsersCount =
                        model.Users.Where(u => u.Vote == ForAgainst.Neutral && u.IsValid)
                            .Select(u => u.UserId)
                            .Distinct()
                            .Count();

                    return model;
                }
            }
        }

        public byte[] GeneratePdf(string id)
        {
            return new IssueGenerator().Generate(GetIssueDocumentModel(id));
        }

        public string CompareVersions(string id, string historyId1, string historyId2)
        {
            var text1 = (from v in GetVersions(id)
                         where v.ObjectId == historyId1
                         select v.VersionText).Single();
            var text2 = (from v in GetVersions(id)
                         where v.ObjectId == historyId2
                         select v.VersionText).Single();


            HtmlDiff diffHelper = new HtmlDiff(text1, text2);
            return diffHelper.Build();
        }
    }
}