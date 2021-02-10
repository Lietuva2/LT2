using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity.Core;
using System.Data.Entity.Validation;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Bus.Commands;
using Data.EF.Actions;
using Data.EF.Users;
using Data.EF.Voting;
using Data.Enums;
using Data.Infrastructure.Sessions;
using Data.MongoDB.Interfaces;
using Data.ViewModels.Base;
using Data.ViewModels.Comments;
using Data.ViewModels.Problem;
using Data.ViewModels.Voting;
using EntityFramework.Extensions;
using Framework;
using Framework.Bus;
using Framework.Enums;
using Framework.Infrastructure;
using Framework.Infrastructure.Logging;
using Framework.Infrastructure.Storage;
using Framework.Infrastructure.ValueInjections;
using Framework.Lists;
using Framework.Mvc.Lists;
using Framework.Mvc.Strings;
using Framework.Other;
using Framework.Strings;
using Globalization.Resources.Services;
using MongoDB.Bson;
using MongoDB.Driver.Builders;

using Omu.ValueInjecter;
using Services.Caching;
using Services.Classes;
using Services.Enums;
using Services.Infrastructure;
using Services.Session;

namespace Services.ModelServices
{
    public class ProblemService : BaseCommentableService, IService
    {
        private readonly IVotingContextFactory votingSessionFactory;
        private readonly IActionsContextFactory actionSessionFactory;
        private readonly Func<INoSqlSession> noSqlSessionFactory;
        private readonly IUsersContextFactory userSessionFactory;
        private readonly CategoryService categoryService;
        private readonly AddressService addressService;
        private readonly SearchService searchService;
        private readonly ILogger logger;

        private readonly IBus bus;
        private readonly ICache cache;
        private const int EMPTY_CATEGORY_ID = 21;

        public OrganizationService OrganizationService
        {
            get { return ServiceLocator.Resolve<OrganizationService>(); }
        }

        public UserService UserService
        {
            get { return ServiceLocator.Resolve<UserService>(); }
        }

        public IdeaService IdeaService
        {
            get { return ServiceLocator.Resolve<IdeaService>(); }
        }

        public ActionService ActionService { get { return ServiceLocator.Resolve<ActionService>(); } }

        public UrlHelper Url
        {
            get { return new UrlHelper(((MvcHandler)HttpContext.Current.Handler).RequestContext); }
        }

        public ProblemService(
            IVotingContextFactory votingSessionFactory,
            IActionsContextFactory actionSessionFactory,
            Func<INoSqlSession> noSqlSessionFactory,
            IUsersContextFactory userSessionFactory,
            CategoryService categoryService,
            AddressService addressService,
            SearchService searchService,
            CommentService commentService,
            ICache cache,
            IBus bus,
            ILogger logger)
            : base(commentService)
        {
            this.votingSessionFactory = votingSessionFactory;
            this.noSqlSessionFactory = noSqlSessionFactory;
            this.actionSessionFactory = actionSessionFactory;
            this.userSessionFactory = userSessionFactory;
            this.categoryService = categoryService;
            this.addressService = addressService;
            this.searchService = searchService;
            this.bus = bus;
            this.cache = cache;
            this.logger = logger;
        }

        public SimpleListContainerModel GetCreatedProblems(string userObjectId, int pageNumber)
        {
            using (var votingSession = votingSessionFactory.CreateContext())
            {

                var result = (from a in votingSession.Problems
                              where
                                  a.UserObjectId == userObjectId && (a.IsPrivateToOrganization == false || CurrentUser.OrganizationIds.Contains(a.OrganizationId))
                              orderby a.Date descending
                              select new SimpleListModel
                                  {
                                      Id = a.Id,
                                      Subject = a.Text,
                                      Date = a.Date
                                  })
                    .GetExpandablePage(pageNumber, CustomAppSettings.PageSizeList).ToList();

                result.ForEach(r => r.Type = EntryTypes.Problem);
                var simpleList = new SimpleListContainerModel();
                simpleList.List = new ExpandableList<SimpleListModel>(result, CustomAppSettings.PageSizeList);
                return simpleList;
            }
        }

        public SimpleListContainerModel GetProblemSupporters(string problemId, int pageNumber)
        {
            using (var session = votingSessionFactory.CreateContext())
            {
                var supporters =
                    session.ProblemSupporters.Where(p => p.ProblemId == problemId).OrderBy(p => p.UserId).Select(
                        p => p.UserId).GetExpandablePage(pageNumber, CustomAppSettings.PageSizeList).ToList();
                var list = UserService.GetUserFullNames(supporters);
                list.ForEach(r => r.Type = EntryTypes.User);
                var simpleList = new SimpleListContainerModel();
                simpleList.List = new ExpandableList<SimpleListModel>(list, CustomAppSettings.PageSizeList);
                return simpleList;
            }
        }

        public void GetProblemInputModel(ProblemIndexModel model)
        {
            if (WorkContext.CategoryIds == null)
            {
                model.Categories = categoryService.GetCategories().ToSelectList();
            }

            if (WorkContext.Municipality == null)
            {
                model.Municipalities = addressService.GetMunicipalities(1);
            }

            model.Organizations = OrganizationService.GetUserOrganizations();
        }

        public ProblemIndexModel GetProblemPage(int pageNumber, ProblemListViews? view, ProblemListSorts sort,
                                                IEnumerable<int> selectedCategories, string organizationId,
                                                string problemId)
        {
            EnsureProblemIndices();
            var model = new ProblemIndexModel();
            List<ProblemIndexItemModel> result;

            GetProblemInputModel(model);

            if (!string.IsNullOrEmpty(problemId))
            {
                model.ProblemId = problemId;
                var problem = GetProblem(problemId);
                if (problem == null)
                {
                    throw new ObjectNotFoundException();
                }

                result = new List<ProblemIndexItemModel>()
                    {
                        GetProblemIndexItem(problem, null, false)
                    };
            }
            else
            {
                if (CurrentUser.IsAuthenticated && selectedCategories == null)
                {
                    selectedCategories = categoryService.GetMyCategoryIds();
                }

                model.SelectedCategories = GetSelectedCategories(selectedCategories);

                var query = GetProblemQuery(view, sort, model.SelectedCategories, model.Organizations, organizationId);

                query = query.Where(q => !q.IsPrivate || CurrentUser.OrganizationIds.Contains(q.OrganizationId));

                model.TotalCount = query.Count();

                var pageSize = CustomAppSettings.PageSizeList;

                if (sort == ProblemListSorts.Newest)
                {
                    query = query.OrderByDescending(i => i.Date);
                }
                else if (sort == ProblemListSorts.MostSupported)
                {
                    query = query.OrderByDescending(i => i.VotesCount).ThenByDescending(i => i.Date);
                }

                result =
                    query.GetExpandablePage(pageNumber, pageSize).Select(i => GetProblemIndexItem(i, null, false, false))
                        .
                        ToList();
            }

            foreach (var r in result)
            {
                r.Categories = r.CategoryIds.Select(cId => new TextValue() { Text = categoryService.GetCategoryName(cId), ValueInt = cId }).ToList();
                r.RelatedIdeas = GetRelatedIdeas(r.Id);
            }

            model.Items = new ExpandableList<ProblemIndexItemModel>(result, CustomAppSettings.PageSizeList);

            return model;
        }

        private List<ProblemIdeaListModel> GetRelatedIdeas(string problemId)
        {
            using (var session = votingSessionFactory.CreateContext())
            {
                return (from pi in session.ProblemIdeas.Where(pp => pp.ProblemId == problemId)
                        where (!pi.Idea.IsDraft || pi.Idea.UserObjectId == CurrentUser.Id) &&
                        (!pi.Idea.IsPrivate || CurrentUser.OrganizationIds.Contains(pi.Idea.OrganizationId))
                        select new ProblemIdeaListModel()
                            {
                                Id = pi.IdeaId,
                                Subject = pi.Idea.Subject,
                                CanDelete =
                                    CurrentUser.IsAuthenticated &&
                                    (pi.UserObjectId == null || pi.UserObjectId == CurrentUser.Id ||
                                     CurrentUser.Role == UserRoles.Admin),
                                ProblemId = problemId
                            }).ToList();
            }
        }

        public ProblemIndexItemModel GetProblemIndexItem(string problemId, string text, bool getChildObjects = false,
                                                         bool renderCollapsed = false)
        {
            return GetProblemIndexItem(GetProblem(problemId), text, getChildObjects, renderCollapsed);
        }

        public IEnumerable<ProblemIndexItemModel> GetProblemIndexItems(List<string> problemIds, UserInfo user)
        {
            if (problemIds.Count == 0)
            {
                return new List<ProblemIndexItemModel>();
            }
            List<ProblemIndexItemModel> result;
            using (var session = votingSessionFactory.CreateContext(unique: true))
            {
                var limit = CustomAppSettings.PageSizeComment + 1;
                result = (from problem in session.Problems
                          where problemIds.Contains(problem.Id)
                          select
                              new ProblemIndexItemModel
                                  {
                                      Id = problem.Id,
                                      Text = problem.Text,
                                      CategoryIds =
                                          problem.ProblemCategories.Select(p => p.CategoryId),
                                      MunicipalityId = problem.MunicipalityId,
                                      Date = problem.Date,
                                      DbComments =
                                          problem.ProblemComments.OrderByDescending(
                                              c => c.Comment.Date)
                                          .Select(c => c.Comment)
                                          .Select(
                                              cc =>
                                              new CommentDbView
                                                  {
                                                      Id = cc.Id,
                                                      Text = cc.Text,
                                                      Date = cc.Date,
                                                      UserObjectId = cc.UserObjectId,
                                                      UserFullName = cc.UserFullName,
                                                      TypeId = cc.TypeId,
                                                      EntryTypeId = cc.EntryTypeId,
                                                      ObjectId = cc.ObjectId,
                                                      Embed = cc.Embed,
                                                      Number = cc.Number,
                                                      SupporterCount =
                                                          cc.CommentSupporters.Count,
                                                      IsLikedByCurrentUser =
                                                          user.IsAuthenticated
                                                          && cc.CommentSupporters.Any(
                                                              s => s.UserId == user.Id)
                                                  })
                                          .Take(limit),
                                      CommentsCount = problem.ProblemComments.Count,
                                      UserObjectId = problem.UserObjectId,
                                      UserFullName = problem.UserFullName,
                                      IsPrivate = problem.IsPrivateToOrganization ?? false,
                                      CanDelete =
                                          user.IsAuthenticated
                                          && (problem.UserObjectId == user.Id
                                              || user.Role == UserRoles.Admin),
                                      Votes =
                                          new VoteResultModel()
                                              {
                                                  Id = problem.Id,
                                                  CurrentSupporter =
                                                      problem.ProblemSupporters
                                                      .FirstOrDefault(
                                                          s => s.UserId == user.Id),
                                                  VotesCount =
                                                      problem.ProblemSupporters
                                                          .Count(
                                                              v =>
                                                              v.ForAgainst
                                                              == (int)ForAgainst.For)
                                                      - problem.ProblemSupporters
                                                            .Count(
                                                                v =>
                                                                v.ForAgainst
                                                                == (int)
                                                                   ForAgainst
                                                                       .Against)
                                              },
                                      RenderCollapsed = false,
                                      OrganizationId = problem.OrganizationId,
                                      OrganizationName = problem.OrganizationName,
                                      RelatedIdeas = from pi in problem.ProblemIdeas
                                                     where
                                                         !pi.Idea.IsDraft
                                                         || pi.Idea.UserObjectId == user.Id
                                                     select
                                                         new ProblemIdeaListModel()
                                                             {
                                                                 Id = pi.IdeaId,
                                                                 Subject =
                                                                     pi.Idea
                                                                     .Subject,
                                                                 CanDelete =
                                                                     user
                                                                         .IsAuthenticated
                                                                     && (string
                                                                             .IsNullOrEmpty
                                                                             (
                                                                                 pi
                                                                             .UserObjectId)
                                                                         || pi
                                                                                .UserObjectId
                                                                         == user
                                                                                .Id
                                                                         || user
                                                                                .Role
                                                                         == UserRoles
                                                                                .Admin),
                                                                 ProblemId =
                                                                     problem.Id
                                                             },
                                      Embed =
                                          new EmbedModel()
                                              {
                                                  Url = problem.Embed.Url,
                                                  Author_Name = problem.Embed.Author_Name,
                                                  Author_Url = problem.Embed.Author_Url,
                                                  Cache_Age = problem.Embed.Cache_Age,
                                                  Description = problem.Embed.Description,
                                                  Height = problem.Embed.Height,
                                                  Html =
                                                      problem.Embed.Type != "link"
                                                          ? problem.Embed.Html
                                                          : string.Empty,
                                                  Provider_Name =
                                                      problem.Embed.Provider_Name,
                                                  Provider_Url =
                                                      problem.Embed.Provider_Url,
                                                  Thumbnail_Url =
                                                      problem.Embed.Thumbnail_Url,
                                                  Title = problem.Embed.Title,
                                                  Type = problem.Embed.Type,
                                                  Version = problem.Embed.Version,
                                                  Width = problem.Embed.Width
                                              }
                                  }).ToList
                    ();
                foreach (var item in result)
                {
                    item.Votes.CurrentVote = item.Votes.CurrentSupporter != null
                                                 ? (ForAgainst)item.Votes.CurrentSupporter.ForAgainst
                                                 : ForAgainst.Neutral;
                    item.Categories =
                        item.CategoryIds.Select(
                            cId => new TextValue() { Text = categoryService.GetCategoryName(cId), ValueInt = cId })
                            .ToList();
                    item.Text = FormatProblemText(item.Text);
                    item.Municipality = addressService.GetMunicipality(item.MunicipalityId);
                    var comments = new List<CommentView>();
                    foreach (var comment in item.DbComments)
                    {
                        comments.Add(commentService.GetCommentView(comment, user, null));
                    }
                    item.Comments = new ExpandableList<CommentView>(comments, CustomAppSettings.PageSizeComment);
                    item.Comments.List = item.Comments.List.OrderBy(c => c.CommentDate).ToList();
                    item.Subscribe = CurrentUser.IsAuthenticated
                                         ? ActionService.IsSubscribed(
                                             item.Id,
                                             CurrentUser.DbId.Value,
                                             EntryTypes.Problem)
                                         : null;
                    item.ProfilePictureThumbId = !string.IsNullOrEmpty(item.OrganizationId) && !item.IsPrivate
                                                     ? OrganizationService.GetProfilePictureThumbId(item.OrganizationId)
                                                     : UserService.GetProfilePictureThumbId(item.UserObjectId);
                }

                return result;
            }
        }

        private string FormatProblemText(string text)
        {
            return text.GetPlainText() == text ? text.NewLineToHtml().ActivateLinks() : text.HtmlDecode();
        }

        public ProblemIndexItemModel GetProblemIndexItem(Data.MongoDB.Problem problem, string text, bool getChildObjects, bool renderCollapsed = false)
        {
            ProblemIndexItemModel model;
            if (problem != null)
            {
                model = new ProblemIndexItemModel
                            {
                                Id = problem.Id,
                                Text = FormatProblemText(problem.Text),
                                CategoryIds = problem.CategoryIds,
                                Municipality = addressService.GetMunicipality(problem.MunicipalityId),
                                Date = problem.Date,
                                Comments = commentService.GetCommentsMostRecent(problem, 0, null, true),
                                CommentsCount = problem.Comments.Count,
                                UserObjectId = problem.UserObjectId,
                                UserFullName = problem.UserFullName,
                                CanDelete = CurrentUser.IsAuthenticated && (problem.UserObjectId == CurrentUser.Id || CurrentUser.Role == UserRoles.Admin),
                                Votes = new VoteResultModel()
                                            {
                                                Id = problem.Id,
                                                CurrentVote = GetCurrentUserVote(problem.Id),
                                                VotesCount = problem.VotesCount
                                            },
                                RenderCollapsed = renderCollapsed,
                                OrganizationId = problem.OrganizationId,
                                OrganizationName = problem.OrganizationName,
                                Subscribe = CurrentUser.IsAuthenticated ? ActionService.IsSubscribed(problem.Id, CurrentUser.DbId.Value, EntryTypes.Problem) : null,
                                IsPrivate = problem.IsPrivate,
                                ProfilePictureThumbId = !string.IsNullOrEmpty(problem.OrganizationId) && !problem.IsPrivate ? OrganizationService.GetProfilePictureThumbId(problem.OrganizationId) : UserService.GetProfilePictureThumbId(problem.UserObjectId)
                            };
                if (problem.Embed != null)
                {
                    model.Embed.InjectFrom(problem.Embed);
                }
                if (getChildObjects)
                {
                    model.Categories = model.CategoryIds.Select(cId => new TextValue() { Text = categoryService.GetCategoryName(cId), ValueInt = cId }).ToList();
                    model.RelatedIdeas = GetRelatedIdeas(problem.Id);
                }
            }
            else
            {
                model = new ProblemIndexItemModel()
                            {
                                Text = text
                            };
            }

            return model;
        }

        private ForAgainst GetCurrentUserVote(string problemId)
        {
            using (var session = votingSessionFactory.CreateContext())
            {
                var result = session.ProblemSupporters.SingleOrDefault(p => p.ProblemId == problemId && p.UserId == CurrentUser.Id);
                if (result == null)
                {
                    return ForAgainst.Neutral;
                }

                return (ForAgainst)result.ForAgainst;
            }
        }

        private IQueryable<Data.MongoDB.Problem> GetProblemQuery(ProblemListViews? view, ProblemListSorts sort, IEnumerable<SelectListItem> selectedCategories, IEnumerable<SelectListItem> userOrganizations, string organizationId)
        {
            IQueryable<Data.MongoDB.Problem> query;
            if (string.IsNullOrEmpty(organizationId))
            {
                var categories = selectedCategories.Where(c => c.Selected).Select(c => Convert.ToInt32(c.Value));
                if (WorkContext.CategoryIds != null)
                {
                    categories = WorkContext.CategoryIds;
                }

                query = GetSelectedCategoriesQuery(categories);


                if (WorkContext.Municipality != null)
                {
                    query = query.Where(q => q.MunicipalityId == WorkContext.Municipality.Id);
                }
                else if (view.HasValue)
                {
                    if (view == ProblemListViews.Subscribed)
                    {
                        var subscribedIdeaIds = UserService.GetSubscribedObjectIds(EntryTypes.Problem);
                        var ids = subscribedIdeaIds.Select(q => BsonObjectId.Create(q)).ToList();
                        using (var session = noSqlSessionFactory())
                        {
                            query = session.GetAllIn<Data.MongoDB.Problem>("_id", ids.ToArray());
                        }
                    }
                    else
                    {
                        var myMuns = GetMyMunicipalities();
                        if (myMuns.Any())
                        {
                            query =
                                query.Where(q => !q.MunicipalityId.HasValue || myMuns.Contains(q.MunicipalityId.Value));

                        }
                        if (view == ProblemListViews.Other)
                        {
                            var ids = query.Select(q => BsonObjectId.Create(q.Id)).ToList();
                            ids.AddRange(UserService.GetSubscribedObjectIds(EntryTypes.Problem).Select(q => BsonObjectId.Create(q)).ToList());
                            using (var session = noSqlSessionFactory())
                            {
                                query = session.GetAllNotIn<Data.MongoDB.Problem>("_id", ids.ToArray());
                            }
                        }
                    }
                }
            }
            else
            {
                query = GetAllProblems().Where(i => i.OrganizationId == organizationId).ToList().AsQueryable();
            }

            //query = query.Where(q => string.IsNullOrEmpty(q.OrganizationId) || userOrganizations.Select(o => o.Value).Contains(q.OrganizationId));

            return query;
        }

        private string GetProblemLink(string problemId)
        {
            return Url.Action("Index", "Problem", new { problemId = problemId });
        }

        public ProblemIndexItemModel Insert(ProblemCreateEditModel model, EmbedModel embed)
        {
            if (WorkContext.CategoryIds != null)
            {
                model.CategoryIds = WorkContext.CategoryIds;
            }
            else
            {
                if (model.CategoryIds == null || !model.CategoryIds.Any())
                {
                    model.CategoryIds = new List<int> {EMPTY_CATEGORY_ID};
                }
            }

            var problem = new Data.MongoDB.Problem
                              {
                                  CategoryIds =
                                      model.CategoryIds,
                                  Date = DateTime.Now,
                                  UserObjectId = CurrentUser.Id,
                                  UserFullName = CurrentUser.FullName,
                                  OrganizationId = model.OrganizationId,
                                  OrganizationName = OrganizationService.GetOrganizationName(model.OrganizationId),
                                  MunicipalityId = model.MunicipalityId,
                                  Id = model.Id,
                                  Text = model.Text,
                                  IsPrivate = model.IsPrivate
                              };

            if (WorkContext.Municipality != null)
            {
                problem.MunicipalityId = WorkContext.Municipality.Id;
            }
            else if (problem.MunicipalityId == 0)
            {
                problem.MunicipalityId = null;
            }

            if (embed != null && !embed.IsEmpty)
            {
                problem.Embed = new Data.MongoDB.Embed();
                problem.Embed.InjectFrom(embed);
                problem.Embed.Title = problem.Embed.Title.Sanitize();
                problem.Embed.Description = problem.Embed.Description.Sanitize();
            }

            using (var noSqlSession = noSqlSessionFactory())
            {
                noSqlSession.Add(problem);
            }

            using (var session = votingSessionFactory.CreateContext(true))
            {
                var dbProblem = new Data.EF.Voting.Problem()
                {
                    Id = problem.Id,
                    Text = problem.Text,
                    Date = problem.Date,
                    MunicipalityId = problem.MunicipalityId,
                    OrganizationId = problem.OrganizationId,
                    UserObjectId = CurrentUser.Id,
                    IsPrivateToOrganization = model.IsPrivate,
                    UserFullName = problem.UserFullName,
                    OrganizationName = problem.OrganizationName
                };
                session.Problems.Add(dbProblem);
            }

            ActionService.Subscribe(problem.Id, CurrentUser.DbId.Value, EntryTypes.Problem);

            var command = new ProblemCommand
                              {
                                  ActionType = ActionTypes.ProblemCreated,
                                  UserId = CurrentUser.Id,
                                  ObjectId = problem.Id.ToString(),
                                  IsPrivate = model.IsPrivate,
                                  Link = GetProblemLink(problem.Id),
                                  Text = problem.Text.RemoveNewLines().Sanitize()
                              };

            bus.Send(command);

            return GetProblemIndexItem(problem, null, true);
        }

        protected override ICommentable GetEntity(MongoObjectId id)
        {
            return GetProblem(id);
        }

        protected override ActionTypes GetAddNewCommentActionType()
        {
            return ActionTypes.ProblemCommented;
        }

        protected override ActionTypes GetLikeCommentActionType()
        {
            return ActionTypes.ProblemCommentLiked;
        }

        protected override void SendCommentCommand(ICommentable entity, ActionTypes actionType, CommentView comment)
        {
            var problem = entity as Data.MongoDB.Problem;
            var relatedUserId = comment.AuthorObjectId;
            if (!string.IsNullOrEmpty(comment.ParentId) && actionType == ActionTypes.CommentCommented)
            {
                relatedUserId =
                    entity.Comments.Where(c => c.Id == comment.ParentId).Select(c => c.UserObjectId).SingleOrDefault();
            }
            bus.Send(new ProblemCommand
            {
                ActionType = actionType,
                ObjectId = comment.EntryId,
                Text = comment.CommentText,
                RelatedUserId = relatedUserId,
                UserId = CurrentUser.Id,
                RelatedObjectId = comment.Id,
                IsPrivate = problem != null && problem.IsPrivate,
                Link = GetProblemLink(comment.EntryId)
            });
        }

        public VoteResultModel Vote(Data.MongoDB.Problem problem, ForAgainst posOrNeg)
        {
            var currentVote = posOrNeg;
            using (var votingSession = votingSessionFactory.CreateContext(true))
            {
                var vote = GetVote(problem.Id, CurrentUser.Id);
                if (vote != null)
                {
                    if (vote.ForAgainst != (int)posOrNeg)
                    {
                        votingSession.ProblemSupporters.Remove(vote);
                        currentVote = ForAgainst.Neutral;
                    }
                }
                else
                {
                    vote = new Data.EF.Voting.ProblemSupporter()
                               {
                                   ProblemId = problem.Id,
                                   UserId = CurrentUser.Id,
                                   ForAgainst = (int)posOrNeg
                               };

                    votingSession.ProblemSupporters.Add(vote);
                }
            }

            UpdateVotesCount(problem);

            return new VoteResultModel()
                {
                    Id = problem.Id,
                    CurrentVote = currentVote,
                    VotesCount = problem.VotesCount,
                    Subscribe = ActionService.Subscribe(problem.Id, CurrentUser.DbId.Value, EntryTypes.Problem)
                };
        }

        private void UpdateVotesCount(Data.MongoDB.Problem problem)
        {
            using (var votingSession = votingSessionFactory.CreateContext(true))
            {
                var id = problem.Id.ToString();
                problem.VotesCount =
                    votingSession.ProblemSupporters.Count(
                        p => p.ProblemId == id && p.ForAgainst == (int)ForAgainst.For) -
                    votingSession.ProblemSupporters.Count(
                        p => p.ProblemId == id && p.ForAgainst == (int)ForAgainst.Against);

                UpdateProblem(problem);
            }
        }

        public int CancelVote(Data.MongoDB.Problem problem)
        {
            var existingVote = GetVote(problem.Id, CurrentUser.Id);

            if (existingVote != null)
            {
                using (var votingSession = votingSessionFactory.CreateContext(true))
                {
                    votingSession.ProblemSupporters.Remove(existingVote);
                }

                UpdateVotesCount(problem);
            }

            return problem.VotesCount;
        }

        public VoteResultModel Vote(MongoObjectId id, ForAgainst posOrNeg)
        {
            var problem = GetProblem(id);
            var initialCount = problem.VotesCount;
            var model = Vote(problem, posOrNeg);
            if (initialCount != model.VotesCount)
            {
                if (model.CurrentVote != ForAgainst.Neutral)
                {
                    bus.Send(new ProblemCommand
                        {
                            ActionType =
                                posOrNeg == ForAgainst.For ? ActionTypes.ProblemVoted : ActionTypes.ProblemDevoted,
                            UserId = CurrentUser.Id,
                            ObjectId = problem.Id.ToString(),
                            IsPrivate = problem.IsPrivate,
                            Link = GetProblemLink(problem.Id),
                            Text = problem.Text
                        });
                }
                else
                {
                    using (var session = actionSessionFactory.CreateContext(true))
                    {
                        var actions =
                            session.Actions.Where(
                                a =>
                                (a.ActionTypeId == (int)ActionTypes.ProblemVoted ||
                                 a.ActionTypeId == (int)ActionTypes.ProblemDevoted) &&
                                a.UserId == CurrentUser.DbId && a.ObjectId == model.Id).ToList();
                        foreach (var action in actions)
                        {
                            action.IsDeleted = true;
                        }
                    }
                }

            }

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

        private Data.EF.Voting.ProblemSupporter GetVote(string problemId, string userId)
        {
            using (var votingSession = votingSessionFactory.CreateContext())
            {
                return votingSession.ProblemSupporters.SingleOrDefault(v => v.ProblemId == problemId && v.UserId == userId);
            }
        }

        public Data.MongoDB.Problem GetProblem(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            using (var noSqlSession = noSqlSessionFactory())
            {
                return noSqlSession.GetById<Data.MongoDB.Problem>(id);
            }
        }

        private void UpdateProblem(Data.MongoDB.Problem problem)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                noSqlSession.Update(problem);
            }
        }

        private IQueryable<Data.MongoDB.Problem> GetSelectedCategoriesQuery(IEnumerable<int> selectedCategories)
        {
            using (var session = noSqlSessionFactory())
            {
                List<BsonValue> arr = new List<BsonValue>();
                foreach (var item in selectedCategories)
                {
                    arr.Add(BsonValue.Create(item));
                }
                return session.GetAllIn<Data.MongoDB.Problem>("CategoryIds", arr.ToArray());
            }
        }

        private IQueryable<Data.MongoDB.Problem> GetSelectedCategoriesInversedQuery(IEnumerable<SelectListItem> selectedCategories)
        {
            using (var session = noSqlSessionFactory())
            {
                List<BsonValue> arr = new List<BsonValue>();
                foreach (var item in selectedCategories)
                {
                    arr.Add(BsonValue.Create(Convert.ToInt32(item.Value)));
                }
                return session.GetAllNotIn<Data.MongoDB.Problem>("CategoryIds", arr.ToArray());
            }
        }

        private void EnsureProblemIndices()
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                noSqlSession.CreateIndex<Data.MongoDB.Problem>("IX_Problem_Date", false, "Date");
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

        private IQueryable<Data.MongoDB.Problem> GetAllProblems()
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                return noSqlSession.GetAll<Data.MongoDB.Problem>();
            }
        }

        public IList<LabelValue> GetMatchedProblems(string prefix)
        {
            using (var session = votingSessionFactory.CreateContext())
            {
                var query = session.Problems.Where(p => p.Text.Contains(prefix));

                return (from q in query
                        select new LabelValue { label = q.Text, value = q.Text, id = q.Id }).ToList();
            }
        }

        public List<ProblemIdeaListModel> AddRelatedIdea(string problemId, string ideaId)
        {
            if (ideaId.IsNullOrEmpty())
            {
                return new List<ProblemIdeaListModel>();
            }

            using (var session = votingSessionFactory.CreateContext(true))
            {
                var pi =
                    session.ProblemIdeas.SingleOrDefault(
                        p => p.IdeaId == ideaId && p.ProblemId == problemId);
                if (pi == null)
                {
                    pi = new ProblemIdea()
                             {
                                 IdeaId = ideaId,
                                 ProblemId = problemId,
                                 UserObjectId = CurrentUser.Id
                             };
                    session.ProblemIdeas.Add(pi);
                    using (var noSqlSession = noSqlSessionFactory())
                    {
                        var idea = noSqlSession.GetSingle<Data.MongoDB.Idea>(i => i.Id == ideaId);

                        var problem = GetProblem(problemId);

                        IdeaService.SaveRelatedIdeas(idea, session.ProblemIdeas.Where(p => p.ProblemId == problemId).Select(p => new RelatedIdeaListItem() { ObjectId = p.IdeaId }).ToList());

                        bus.Send(new ProblemCommand()
                        {
                            ActionType = ActionTypes.ProblemIdeaAssigned,
                            UserId = CurrentUser.Id,
                            ObjectId = problemId,
                            Link = GetProblemLink(problem.Id),
                            RelatedSubject = idea.Subject,
                            RelatedObjectId = ideaId,
                            RelatedLink = Url.Action("Details", "Idea", new { id = ideaId }),
                            MessageDate = DateTime.Now
                        });

                        return new List<ProblemIdeaListModel>(){ new ProblemIdeaListModel()
                                          {
                                              Id = idea.Id,
                                              Subject = idea.Subject,
                                              CanDelete = true
                                          }};
                    }
                }
            }

            return new List<ProblemIdeaListModel>();
        }

        public bool Delete(string problemId)
        {
            using (var session = votingSessionFactory.CreateContext(true))
            {
                if (CurrentUser.Role != UserRoles.Admin)
                {
                    var supporters = session.ProblemSupporters.Any(p => p.ProblemId == problemId);
                    if (supporters)
                    {
                        return false;
                    }
                }

                session.ProblemIdeas.Delete(p => p.ProblemId == problemId);
                session.ProblemIssues.Delete(p => p.ProblemId == problemId);
                session.ProblemCategories.Delete(p => p.ProblemId == problemId);
                var comments = session.ProblemComments.Where(c => c.ProblemId == problemId)
                                     .Select(p => p.Comment)
                                     .ToList();
                foreach (var comment in comments)
                {
                    session.Comments.Remove(comment);
                }

                session.ProblemComments.Delete(p => p.ProblemId == problemId);
                session.Problems.Delete(p => p.Id == problemId);
                try
                {
                    session.SaveChanges();
                }
                catch
                {
                    return false;
                }
            }

            using (var session = noSqlSessionFactory())
            {
                session.Delete<Data.MongoDB.Problem>(p => p.Id == problemId);
            }

            using (var session = actionSessionFactory.CreateContext(true))
            {
                var actions = session.Actions.Where(a => a.ObjectId == problemId);
                foreach (var a in actions)
                {
                    a.IsDeleted = true;
                }
            }

            return true;
        }

        public bool DeleteRelatedIdea(string ideaId, string problemId)
        {
            using (var session = votingSessionFactory.CreateContext(true))
            {
                session.ProblemIdeas.Delete(p => p.ProblemId == problemId && p.IdeaId == ideaId);
            }

            return true;
        }

        public bool UpdateProblemDb()
        {
            using (var session = noSqlSessionFactory())
            {
                foreach (var problem in session.GetAll<Data.MongoDB.Problem>())
                {
                    UpdateDbProblem(problem);
                }
            }

            return true;
        }

        public void UpdateDbProblem(string problemId)
        {
            UpdateDbProblem(GetProblem(problemId));
        }

        public void UpdateDbProblem(Data.MongoDB.Problem problem)
        {
            using (var votingSession = votingSessionFactory.CreateContext(true))
            {
                string problemId = problem.Id.ToString();
                var dbProblem = votingSession.Problems.SingleOrDefault(i => i.Id == problemId);
                if (dbProblem == null)
                {
                    dbProblem = new Data.EF.Voting.Problem()
                        {
                            Id = problem.Id
                        };
                    votingSession.Problems.Add(dbProblem);
                }

                dbProblem.Id = problem.Id;
                dbProblem.Text = problem.Text;
                dbProblem.Date = problem.Date.Truncate();
                dbProblem.MunicipalityId = problem.MunicipalityId;
                dbProblem.OrganizationId = !string.IsNullOrEmpty(problem.OrganizationId) ? problem.OrganizationId : null;
                dbProblem.UserObjectId = problem.UserObjectId;
                dbProblem.UserFullName = problem.UserFullName;
                dbProblem.OrganizationName = problem.OrganizationName;
                dbProblem.IsPrivateToOrganization = problem.IsPrivate;

                var dbCategories = votingSession.ProblemCategories.Where(ic => ic.ProblemId == problemId).ToList();
                var deletedCategoryIds = dbCategories.Select(
                    ic => ic.CategoryId).Except(problem.CategoryIds.Select(ri => ri)).ToList();
                if (deletedCategoryIds.Any())
                {
                    votingSession.ProblemCategories.Delete(
                        c => deletedCategoryIds.Contains(c.CategoryId) && c.ProblemId == problemId);
                }

                foreach (var categoryId in problem.CategoryIds)
                {
                    if (!dbCategories.Any(c => c.CategoryId == categoryId))
                    {
                        dbProblem.ProblemCategories.Add(new ProblemCategory()
                        {
                            CategoryId = categoryId
                        });
                    }
                }

                //votingSession.ProblemCategories.Delete(ic => ic.ProblemId == problemId);
                //foreach (var cat in problem.CategoryIds)
                //{
                //    dbProblem.ProblemCategories.Add(new ProblemCategory()
                //        {
                //            CategoryId = cat
                //        });
                //}

                if (problem.Embed != null)
                {

                    if (dbProblem.Embed == null)
                    {
                        dbProblem.Embed = new Embed();
                    }
                    var embed = votingSession.Embeds.SingleOrDefault(e => e.Id == dbProblem.EmbedId);
                    dbProblem.Embed = embed ?? new Data.EF.Voting.Embed();

                    dbProblem.Embed.Author_Name = problem.Embed.Author_Name;
                    dbProblem.Embed.Author_Url = problem.Embed.Author_Url;
                    dbProblem.Embed.Cache_Age = problem.Embed.Cache_Age;
                    dbProblem.Embed.Description = problem.Embed.Description;
                    dbProblem.Embed.Height = problem.Embed.Height;
                    dbProblem.Embed.Html = problem.Embed.Html;
                    dbProblem.Embed.Provider_Name = problem.Embed.Provider_Name;
                    dbProblem.Embed.Provider_Url = problem.Embed.Provider_Url;
                    dbProblem.Embed.Thumbnail_Url = problem.Embed.Thumbnail_Url;
                    dbProblem.Embed.Title = problem.Embed.Title;
                    dbProblem.Embed.Type = problem.Embed.Type;
                    dbProblem.Embed.Url = problem.Embed.Url;
                    dbProblem.Embed.Version = problem.Embed.Version;
                    dbProblem.Embed.Width = problem.Embed.Width;
                }

                try
                {
                    votingSession.SaveChanges();
                }
                catch (DbEntityValidationException ex)
                {
                    logger.Error(ex.EntityValidationErrors.SelectMany(e => e.ValidationErrors).Select(e => e.ErrorMessage).Concatenate(", "), ex);
                    throw;
                }

                commentService.UpdateComments(problem.Id, problem.Comments, EntryTypes.Problem);
            }
        }
    }
}