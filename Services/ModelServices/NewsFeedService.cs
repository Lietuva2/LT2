using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Security;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using System.Web.Services.Description;
using Bus.Commands;
using Data.EF.Actions;
using Data.EF.Voting;
using Data.Enums;
using Data.Infrastructure.Sessions;
using Data.MongoDB;
using Data.ViewModels.Account;
using Data.ViewModels.Base;
using Data.ViewModels.Comments;
using Data.ViewModels.NewsFeed;
using Data.ViewModels.Problem;
using Data.ViewModels.Voting;
using EntityFramework.Extensions;
using Framework;
using Framework.Bus;
using Framework.Data;
using Framework.Enums;
using Framework.Infrastructure;
using Framework.Infrastructure.Logging;
using Framework.Infrastructure.Storage;
using Framework.Lists;
using Framework.Other;
using Framework.Strings;
using Globalization;
using LinqKit;
using MongoDB.Bson;
using MongoDB.Driver.Builders;

using Ninject;
using Services.Caching;
using Services.Enums;
using Services.Infrastructure;
using Services.Session;
using Action = System.Action;
using Idea = Data.MongoDB.Idea;

namespace Services.ModelServices
{
    public class NewsFeedService : IService
    {
        private readonly IActionsContextFactory actionSessionFactory;
        private readonly IVotingContextFactory votingSessionFactory;
        private readonly Func<INoSqlSession> noSqlSessionFactory;
        private readonly IBus bus;
        private readonly ICache cache;
        private readonly ILogger logger;
        private ProblemService problemService;
        private CategoryService categoryService;
        private CommentService commentService;

        public ProblemService ProblemService
        {
            get { return problemService ?? (problemService = ServiceLocator.Resolve<ProblemService>()); }
        }

        public CategoryService CategoryService
        {
            get { return categoryService ?? (categoryService = ServiceLocator.Resolve<CategoryService>()); }
        }

        public CommentService CommentService
        {
            get { return commentService ?? (commentService = ServiceLocator.Resolve<CommentService>()); }
        }

        public IdeaService IdeaService { get { return ServiceLocator.Resolve<IdeaService>(); } }
        public VotingService VotingService { get { return ServiceLocator.Resolve<VotingService>(); } }
        public UserService UserService { get { return ServiceLocator.Resolve<UserService>(); } }
        public OrganizationService OrganizationService { get { return ServiceLocator.Resolve<OrganizationService>(); } }

        private int pageSize = CustomAppSettings.PageSizeNewsFeed;
        public int PageSizeNewsFeed { get { return pageSize; } set { pageSize = value; } }
        public UserInfo CurrentUser { get { return MembershipSession.GetUser(); } }

        public List<string> MyProjects
        {
            get
            {
                if (CurrentUser == null)
                {
                    return null;
                }

                if (CurrentUser.Projects == null)
                {
                    if (!CurrentUser.IsAuthenticated)
                    {
                        return new List<string>();
                    }

                    CurrentUser.Projects = GetCurrentUserProjects();
                }

                return CurrentUser.Projects;
            }
        }

        public NewsFeedService(
            IActionsContextFactory actionSessionFactory,
            Func<INoSqlSession> noSqlSessionFactory,
            IVotingContextFactory votingSessionFactory,
            ICache cache,
            IBus bus,
            ILogger logger)
        {
            this.noSqlSessionFactory = noSqlSessionFactory;
            this.actionSessionFactory = actionSessionFactory;
            this.votingSessionFactory = votingSessionFactory;
            this.bus = bus;
            this.cache = cache;
            this.logger = logger;
        }

        private List<string> GetCurrentUserProjects()
        {
            if (CurrentUser.IsAuthenticated)
            {
                return GetUserProjects(CurrentUser.Id);
            }

            return new List<string>();
        }

        private List<string> GetUserProjects(string userId)
        {
            if (!string.IsNullOrEmpty(userId))
            {
                using (var session = noSqlSessionFactory())
                {
                    var projs = new List<string>();
                    foreach (var proj in session.Find<Data.MongoDB.Project>(Query.EQ("ProjectMembers.UserObjectId", userId)).SetFields("_id").ToList().Select(p => p.Id).ToList())
                    {
                        projs.Add(proj.ToString());
                    }

                    return projs;
                }
            }

            return new List<string>();
        }

        /// <summary>
        /// RSS
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="lastQueryDate"></param>
        /// <returns></returns>
        public NewsFeedIndexModel GetNewsFeedPage(int userId, DateTime? lastQueryDate = null)
        {
            var model = GetNewsFeedList(0, NewsFeedListViews.MyNews, lastQueryDate, userId);
            model.List.List =
                model.List.List.Where(
                    l =>
                    l.IsRead == false && l.ActionTypeId != (int)ActionTypes.IdeaVersionLiked &&
                    l.ActionTypeId != (int)ActionTypes.Voted);
            return model;
        }

        public NewsFeedIndexModel GetNewsFeedPage(int pageNumber, NewsFeedListViews view, DateTime? lastQueryDate = null)
        {
            return GetNewsFeedPage(pageNumber, view, lastQueryDate, CurrentUser.DbId);
        }

        public int GetUnreadNewsCount()
        {
            return GetUnreadNewsCount(CurrentUser.DbId);
        }

        public int GetUnreadNewsCount(int? userId)
        {
            if (!userId.HasValue)
            {
                return 0;
            }

            using (var actionSession = actionSessionFactory.CreateContext())
            {
                return actionSession.Notifications.Count(n => n.UserId == userId && !n.Action.IsGrouped && n.Action.ActionTypeId != (int)ActionTypes.Voted && n.Action.ActionTypeId != (int)ActionTypes.IdeaVersionLiked && !n.IsRead);
            }
        }

        public Task<NewsFeedIndexModel> GetNewsFeedPageAsync(int pageNumber, NewsFeedListViews view, DateTime? lastQueryDate = null)
        {
            return GetNewsFeedPageAsync(pageNumber, view, lastQueryDate, CurrentUser.DbId, !lastQueryDate.HasValue);
        }

        private NewsFeedIndexModel GetNewsFeedList(int? pageNumber, NewsFeedListViews view, DateTime? lastQueryDate, int? currentUserId)
        {
            var model = new NewsFeedIndexModel();
            IQueryable<NewsFeedItemModel> query;
            ExpandableList<NewsFeedItemModel> list;
            if (currentUserId == 0)
            {
                currentUserId = null;
            }

            using (var actionSession = actionSessionFactory.CreateContext())
            {
                if (view == NewsFeedListViews.MyNews)
                {
                    query = from n in actionSession.Notifications.Where(n => n.UserId == currentUserId)
                            join a in actionSession.Actions.Where(aa => !aa.IsDeleted) on n.ActionId equals a.Id
                            orderby n.Action.Date descending
                            select new NewsFeedItemModel
                            {
                                ActionTypeId = a.ActionTypeId,
                                ActionTypeName = a.ActionTypeName,
                                Date = a.Date,
                                UserDbId = a.UserId,
                                UserObjectId = a.UserObjectId,
                                UserFullName = a.UserFullName,
                                Subject = a.Subject,
                                ObjectId = a.ObjectId,
                                RelatedUserFullName = a.RelatedUserFullName,
                                RelatedUserObjectId = a.RelatedUserObjectId,
                                Text = a.Text,
                                RawText = a.Text,
                                RelatedObjectId = a.RelatedObjectId,
                                RelatedRelatedObjectId = a.RelatedRelatedObjectId,
                                NewsFeedTypeId = (int)NewsFeedTypes.NewsFeed,
                                EntryTypeId = a.EntryTypeId,
                                IsRead = n.IsRead,
                                UserCount = null,
                                RelatedLink = a.RelatedLink,
                                RelatedSubject = a.RelatedSubject,
                                Link = a.Link,
                                OrganizationId = a.OrganizationId,
                                OrganizationName = a.OrganizationName,
                                IsPrivate = a.IsPrivate,
                                MunicipalityId = a.MunicipalityId,
                                GroupId = a.GroupId,
                                CategoryIds = a.ActionCategories.Select(c => c.CategoryId)
                            };
                }
                else
                {
                    query = from n in actionSession.Actions
                            where !n.IsDeleted && (view != NewsFeedListViews.AllNews || !n.IsGrouped)
                            orderby n.Date descending
                            select new NewsFeedItemModel
                            {
                                ActionTypeId = n.ActionTypeId,
                                ActionTypeName = n.ActionTypeName,
                                Date = n.Date,
                                UserDbId = n.UserId,
                                UserObjectId = n.UserObjectId,
                                UserFullName = n.UserFullName,
                                Subject = n.Subject,
                                ObjectId = n.ObjectId,
                                RelatedUserFullName = n.RelatedUserFullName,
                                RelatedUserObjectId = n.RelatedUserObjectId,
                                RelatedObjectId = n.RelatedObjectId,
                                RelatedRelatedObjectId = n.RelatedRelatedObjectId,
                                Text = n.Text,
                                RawText = n.Text,
                                NewsFeedTypeId = (int)NewsFeedTypes.AllNews,
                                EntryTypeId = n.EntryTypeId,
                                UserCount = null,
                                RelatedLink = n.RelatedLink,
                                RelatedSubject = n.RelatedSubject,
                                Link = n.Link,
                                OrganizationId = n.OrganizationId,
                                OrganizationName = n.OrganizationName,
                                IsPrivate = n.IsPrivate,
                                MunicipalityId = n.MunicipalityId,
                                CategoryIds = n.ActionCategories.Select(c => c.CategoryId)
                            };
                }

                query =
                    query.Where(
                        n =>
                        n.ActionTypeId != (int)ActionTypes.UserProfileViewed &&
                        n.ActionTypeId != (int)ActionTypes.IdeaViewed &&
                        n.ActionTypeId != (int)ActionTypes.IssueViewed &&
                        n.ActionTypeId != (int)ActionTypes.OrganizationViewed &&
                        n.ActionTypeId != (int)ActionTypes.LikedCategory);

                if (lastQueryDate.HasValue)
                {
                    query = query.Where(q => q.Date > lastQueryDate.Value);
                    //if (view == NewsFeedListViews.MyNews)
                    //{
                    //    query = query.Where(q => q.Date > lastQueryDate.Value || q.IsRead == false);
                    //}
                    //else
                    //{
                    //    query = query.Where(q => q.Date > lastQueryDate.Value);
                    //}
                }

                if (view == NewsFeedListViews.PolNews)
                {
                    var politicianIds = UserService.GetPoliticianIds();
                    query = query.Where(n => politicianIds.Contains(n.UserDbId));
                }

                list = GetNewsPage(query, pageNumber, currentUserId);
            }

            if (view != NewsFeedListViews.AllNews)
            {
                var groupedItems = (from l in list.List
                                    group l by l.GroupId
                                        into g
                                    select new { Key = g.Key, Items = g.OrderByDescending(i => i.Date) }).ToList();

                if (groupedItems.Count() > 1)
                {
                    var groupedList = new List<NewsFeedItemModel>();
                    foreach (var group in groupedItems)
                    {
                        if (group.Key != null)
                        {
                            groupedList.Add(group.Items.First());
                        }
                        else
                        {
                            groupedList.AddRange(group.Items);
                        }
                    }

                    list.List = groupedList;
                }
            }

            foreach (var item in list.List)
            {
                GetGroupedItems(item, view);
            }

            model.List = list;

            return model;
        }

        private async Task<NewsFeedIndexModel> GetNewsFeedPageAsync(
            int? pageNumber,
            NewsFeedListViews view,
            DateTime? lastQueryDate,
            int? currentUserId,
            bool groupItems)
        {
            var model = GetNewsFeedList(pageNumber, view, lastQueryDate, currentUserId);

            List<Task> tasks = new List<Task>();

            var user = CurrentUser;
            categoryService = CategoryService;
            problemService = ProblemService;
            commentService = CommentService;
            tasks.Add(Task.Run(() => GetCommentModel(model.List.List, user)));
            tasks.Add(Task.Run(() => GetProblemModel(model.List.List, user)));

            if (view == NewsFeedListViews.AllNews && groupItems)
            {
                tasks.Add(
                    Task.Run(
                        () => model.List.List = GroupItems(model.List.List.ToList(), view == NewsFeedListViews.AllNews)));
            }

            //make sure extra boxes are only loaded once
            if (pageNumber == 0 && !lastQueryDate.HasValue)
            {
                if (currentUserId.HasValue)
                {
                    GetProblemInputModel(model);

                    tasks.Add(Task.Run(() => model.UrgentMessages.AddRange(GetUnreadVersionsOfVotedIdeas(user))));
                    tasks.Add(Task.Run(() => model.UrgentMessages.AddRange(GetMyProjects(user))));
                    tasks.Add(Task.Run(() => model.UrgentMessages.AddRange(GetMyOrganizations(user))));
                    tasks.Add(Task.Run(() => model.UrgentMessages.AddRange(GetRandomUnreadIdeas(user))));
                    tasks.Add(Task.Run(() => model.UrgentMessages.AddRange(GetRandomUnreadIssues(user))));
                }
            }

            await Task.WhenAll(tasks);

            return model;
        }

        private NewsFeedIndexModel GetNewsFeedPage(
            int? pageNumber,
            NewsFeedListViews view,
            DateTime? lastQueryDate,
            int? currentUserId)
        {
            var model = GetNewsFeedList(pageNumber, view, lastQueryDate, currentUserId);

            GetCommentModel(model.List.List, CurrentUser);
            GetProblemModel(model.List.List, CurrentUser);

            if (model.List.List.Any() && currentUserId.HasValue)
            {
                bus.Send(
                    new NotificationViewedCommand
                    {
                        NotifyUserDbId = currentUserId.Value,
                        LastViewedId = model.List.List.Min(l => l.Id)
                    });
            }

            //make sure extra boxes are only loaded once
            if (pageNumber == 0 && !lastQueryDate.HasValue)
            {
                if (currentUserId.HasValue)
                {
                    model.UrgentMessages.AddRange(GetUnreadVersionsOfVotedIdeas(CurrentUser));
                    model.UrgentMessages.AddRange(GetMyProjects(CurrentUser));
                    model.UrgentMessages.AddRange(GetMyOrganizations(CurrentUser));
                    model.UrgentMessages.AddRange(GetRandomUnreadIdeas(CurrentUser));
                    model.UrgentMessages.AddRange(GetRandomUnreadIssues(CurrentUser));
                    GetProblemInputModel(model);
                }
            }

            model.List.List = GroupVotes(model.List.List.ToList());

            model.List.List = GroupItems(model.List.List.ToList(), view == NewsFeedListViews.AllNews);

            model.List = model.List;

            return model;
        }

        private void GetProblemInputModel(NewsFeedIndexModel model)
        {
            model.ProblemInput = new ProblemIndexModel();
            ProblemService.GetProblemInputModel(model.ProblemInput);
        }


        public ExpandableList<NewsFeedItemModel> GetOrganizationActivityPage(string organizationId, int pageNumber)
        {
            using (var actionSession = actionSessionFactory.CreateContext())
            {
                var query = from n in actionSession.Actions
                            where !n.IsDeleted && n.OrganizationId == organizationId
                            orderby n.Date descending
                            select new NewsFeedItemModel
                            {
                                ActionTypeId = n.ActionTypeId,
                                ActionTypeName = n.ActionTypeName,
                                Date = n.Date,
                                UserDbId = n.UserId,
                                UserObjectId = n.UserObjectId,
                                UserFullName = n.UserFullName,
                                Subject = n.Subject,
                                ObjectId = n.ObjectId,
                                RelatedUserFullName = n.RelatedUserFullName,
                                RelatedUserObjectId = n.RelatedUserObjectId,
                                RelatedObjectId = n.RelatedObjectId,
                                RelatedRelatedObjectId = n.RelatedRelatedObjectId,
                                Text = n.Text,
                                RawText = n.Text,
                                NewsFeedTypeId = (int)NewsFeedTypes.AllNews,
                                EntryTypeId = n.EntryTypeId,
                                UserCount = null,
                                RelatedLink = n.RelatedLink,
                                RelatedSubject = n.RelatedSubject,
                                Link = n.Link,
                                OrganizationId = n.OrganizationId,
                                OrganizationName = n.OrganizationName,
                                IsPrivate = n.IsPrivate,
                                MunicipalityId = n.MunicipalityId,
                                CategoryIds = n.ActionCategories.Select(c => c.CategoryId)
                            };

                //var query = from n in actionSession.OrganizationActionViews
                //            where n.OrganizationId == organizationId
                //            orderby n.Date descending
                //            select new NewsFeedItemModel
                //            {
                //                ActionTypeId = n.ActionTypeId,
                //                ActionTypeName = n.ActionTypeName,
                //                Date = n.Date.Value,
                //                UserDbId = n.UserId,
                //                UserObjectId = n.UserObjectId,
                //                UserFullName = n.UserFullName,
                //                Subject = n.Subject,
                //                ObjectId = n.ObjectId,
                //                RelatedUserFullName = n.RelatedUserFullName,
                //                RelatedUserObjectId = n.RelatedUserObjectId,
                //                RelatedObjectId = n.RelatedObjectId,
                //                Text = n.Text,
                //                RawText = n.Text,
                //                NewsFeedTypeId = (int)NewsFeedTypes.OrganizationActivity,
                //                EntryTypeId = n.EntryTypeId,
                //                RelatedLink = n.RelatedLink,
                //                RelatedSubject = n.RelatedSubject,
                //                Link = n.Link,
                //                OrganizationId = n.OrganizationId,
                //                OrganizationName = n.OrganizationName,
                //                UserCount = n.UserCount,
                //                IsPrivate = n.IsPrivate,
                //                MunicipalityId = n.MunicipalityId,
                //            };

                var list = GetNewsPage(query, pageNumber, CurrentUser.DbId);

                foreach (var item in list.List)
                {
                    GetGroupedItems(item, null);
                }

                return list;
            }
        }

        public ExpandableList<NewsFeedItemModel> GetUserReputationPage(string userObjectId, int pageNumber)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                if (!string.IsNullOrEmpty(userObjectId))
                {
                    var user = noSqlSession.GetSingle<User>(u => u.Id == userObjectId);
                    if (!UserService.GetIsVisible(user, user.Settings.DetailsVisibility.Reputation))
                    {
                        throw new SecurityException("Activity is not visible");
                    }
                }
            }

            using (var actionSession = actionSessionFactory.CreateContext())
            {
                var query = from n in actionSession.Actions
                            join t in actionSession.ActionTypes on n.ActionTypeId equals t.Id
                            where n.LikedUserObjectId == userObjectId && t.Reputation.HasValue && !n.IsDeleted
                            orderby n.Date descending
                            select new NewsFeedItemModel
                            {
                                ActionTypeId = n.ActionTypeId,
                                ActionTypeName = n.ActionTypeName,
                                Date = n.Date,
                                UserDbId = n.UserId,
                                UserObjectId = n.UserObjectId,
                                UserFullName = n.UserFullName,
                                Subject = n.Subject,
                                ObjectId = n.ObjectId,
                                RelatedUserFullName = n.RelatedUserFullName,
                                RelatedUserObjectId = n.RelatedUserObjectId,
                                RelatedObjectId = n.RelatedObjectId,
                                RelatedRelatedObjectId = n.RelatedRelatedObjectId,
                                Text = n.Text,
                                RawText = n.Text,
                                NewsFeedTypeId = (int)NewsFeedTypes.AllNews,
                                EntryTypeId = n.EntryTypeId,
                                UserCount = null,
                                RelatedLink = n.RelatedLink,
                                RelatedSubject = n.RelatedSubject,
                                Link = n.Link,
                                OrganizationId = n.OrganizationId,
                                OrganizationName = n.OrganizationName,
                                IsPrivate = n.IsPrivate,
                                MunicipalityId = n.MunicipalityId,
                                CategoryIds = n.ActionCategories.Select(c => c.CategoryId)
                            };
                //var query = from n in actionSession.ActionViews
                //            join t in actionSession.ActionTypes on n.ActionTypeId equals t.Id
                //            where n.LikedUserObjectId == userObjectId && t.Reputation.HasValue
                //            orderby n.Date descending
                //            select new NewsFeedItemModel
                //            {
                //                ActionTypeId = n.ActionTypeId,
                //                ActionTypeName = n.ActionTypeName,
                //                Date = n.Date.Value,
                //                UserDbId = n.UserId,
                //                UserObjectId = n.UserObjectId,
                //                UserFullName = n.UserFullName,
                //                Subject = n.Subject,
                //                ObjectId = n.ObjectId,
                //                RelatedUserFullName = n.RelatedUserFullName,
                //                RelatedUserObjectId = n.RelatedUserObjectId,
                //                RelatedObjectId = n.RelatedObjectId,
                //                Text = n.Text,
                //                RawText = n.Text,
                //                NewsFeedTypeId = (int)NewsFeedTypes.UserReputation,
                //                EntryTypeId = n.EntryTypeId,
                //                RelatedLink = n.RelatedLink,
                //                RelatedSubject = n.RelatedSubject,
                //                Link = n.Link,
                //                UserCount = n.UserCount,
                //                OrganizationId = n.OrganizationId,
                //                OrganizationName = n.OrganizationName,
                //                IsPrivate = n.IsPrivate,
                //                MunicipalityId = n.MunicipalityId,
                //                Reputation = t.Reputation
                //            };

                var list = GetNewsPage(query, pageNumber, CurrentUser.DbId);

                foreach (var item in list.List)
                {
                    GetGroupedItems(item, null);
                }

                return list;
            }
        }

        public ExpandableList<NewsFeedItemModel> GetUserActivityPage(string userObjectId, int pageNumber)
        {
            int userDbId;
            using (var noSqlSession = noSqlSessionFactory())
            {
                var user = noSqlSession.GetSingle<User>(u => u.Id == userObjectId);
                userDbId = user.DbId;
                if (!UserService.GetIsVisible(user, user.Settings.DetailsVisibility.Activity))
                {
                    throw new SecurityException("Activity is not visible");
                }
            }

            using (var actionSession = actionSessionFactory.CreateContext())
            {
                var query = from n in actionSession.Actions
                            where !n.IsDeleted && n.UserId == userDbId
                            orderby n.Date descending
                            select new NewsFeedItemModel
                            {
                                ActionTypeId = n.ActionTypeId,
                                ActionTypeName = n.ActionTypeName,
                                Date = n.Date,
                                UserDbId = n.UserId,
                                UserObjectId = n.UserObjectId,
                                UserFullName = n.UserFullName,
                                Subject = n.Subject,
                                ObjectId = n.ObjectId,
                                RelatedUserFullName = n.RelatedUserFullName,
                                RelatedUserObjectId = n.RelatedUserObjectId,
                                RelatedObjectId = n.RelatedObjectId,
                                RelatedRelatedObjectId = n.RelatedRelatedObjectId,
                                Text = n.Text,
                                RawText = n.Text,
                                NewsFeedTypeId = (int)NewsFeedTypes.AllNews,
                                EntryTypeId = n.EntryTypeId,
                                UserCount = null,
                                RelatedLink = n.RelatedLink,
                                RelatedSubject = n.RelatedSubject,
                                Link = n.Link,
                                OrganizationId = n.OrganizationId,
                                OrganizationName = n.OrganizationName,
                                IsPrivate = n.IsPrivate,
                                MunicipalityId = n.MunicipalityId,
                                CategoryIds = n.ActionCategories.Select(c => c.CategoryId)
                            };
                //var query = from n in actionSession.ActionViews
                //            where n.UserId == userDbId
                //            orderby n.Date descending
                //            select new NewsFeedItemModel
                //            {
                //                ActionTypeId = n.ActionTypeId,
                //                ActionTypeName = n.ActionTypeName,
                //                Date = n.Date.Value,
                //                UserDbId = n.UserId,
                //                UserObjectId = n.UserObjectId,
                //                UserFullName = n.UserFullName,
                //                Subject = n.Subject,
                //                ObjectId = n.ObjectId,
                //                RelatedUserFullName = n.RelatedUserFullName,
                //                RelatedUserObjectId = n.RelatedUserObjectId,
                //                RelatedObjectId = n.RelatedObjectId,
                //                Text = n.Text,
                //                RawText = n.Text,
                //                NewsFeedTypeId = (int)NewsFeedTypes.UserActivity,
                //                EntryTypeId = n.EntryTypeId,
                //                RelatedLink = n.RelatedLink,
                //                RelatedSubject = n.RelatedSubject,
                //                Link = n.Link,
                //                UserCount = n.UserCount,
                //                OrganizationId = n.OrganizationId,
                //                OrganizationName = n.OrganizationName,
                //                IsPrivate = n.IsPrivate,
                //                MunicipalityId = n.MunicipalityId
                //            };

                query =
                    query.Where(
                        n =>
                        n.ActionTypeId != (int)ActionTypes.UserProfileViewed);

                var list = GetNewsPage(query, pageNumber, CurrentUser.DbId);
                list.List = GroupViews(list.List.ToList());

                foreach (var item in list.List)
                {
                    GetGroupedItems(item, null);
                }

                return list;
            }
        }

        public ExpandableList<NewsFeedItemModel> GetNewsPage(IQueryable<NewsFeedItemModel> query, int? pageNumber, int? currentUserId)
        {
            var orgs = UserService.GetUserOrganizationIds(currentUserId);
            var projs = MyProjects ?? GetUserProjects(UserService.GetUserObjectId(currentUserId));

            query = query.Where(q => q.IsPrivate == false || (currentUserId.HasValue && ((string.IsNullOrEmpty(q.OrganizationId) && projs.Contains(q.ObjectId)) || orgs.Contains(q.OrganizationId))));

            if (WorkContext.CategoryIds != null)
            {
                var predicate = PredicateBuilder.False<NewsFeedItemModel>();
                foreach (var cat in WorkContext.CategoryIds)
                {
                    var shortCat = (short)cat;
                    predicate = predicate.Or(c => c.CategoryIds.Contains(shortCat));
                }

                query = query.AsExpandable().Where(predicate);
            }

            if (WorkContext.Municipality != null)
            {
                var munId = WorkContext.Municipality.Id;
                query = query.Where(q => q.MunicipalityId == munId);
            }

            if (pageNumber.HasValue)
            {
                query = query.GetExpandablePage(pageNumber.Value, PageSizeNewsFeed);
            }

            var list = new ExpandableList<NewsFeedItemModel>(query.ToList(), PageSizeNewsFeed);
            var id = 1;
            foreach (var item in list.List)
            {
                var it = item;

                item.Text = item.Text.NewLineToHtml().ActivateLinks();

                if ((item.EntryType ?? EntryTypes.User) == EntryTypes.User ||
                    item.ActionTypeId.In((int)ActionTypes.IdeaCreated, (int)ActionTypes.IdeaVersionAdded,
                                         (int)ActionTypes.IssueCreated) ||
                    item.EntryType == EntryTypes.Organization && !string.IsNullOrEmpty(item.OrganizationId))
                {
                    item.ProfilePictureThumbId = !string.IsNullOrEmpty(item.OrganizationId) ? OrganizationService.GetProfilePictureThumbId(item.OrganizationId) : UserService.GetProfilePictureThumbId(item.UserObjectId);
                }

                if (item.ActionTypeId == (int)ActionTypes.LikedCategory)
                {
                    using (var actionSession = actionSessionFactory.CreateContext())
                    {
                        var dateFrom = it.Date.Date;
                        var dateTo = item.Date.Date.AddDays(1);
                        var objectIds = (from a in actionSession.Actions
                                         where a.UserId == it.UserDbId &&
                                               a.ActionTypeId == (int)ActionTypes.LikedCategory &&
                                               a.Date >= dateFrom && a.Date < dateTo && !a.IsDeleted
                                         select a.ObjectId).Distinct().ToList();

                        var categoryIds = item.CategoryIds.ToList();
                        foreach (var objId in objectIds)
                        {
                            categoryIds.Add(Convert.ToInt16(objId));
                        }

                        item.CategoryNames = (from c in actionSession.Categories
                                              where categoryIds.Contains(c.Id)
                                              select c.Name).Distinct().ToList();
                    }
                }

                item.TimePassed = GlobalizedSentences.GetTimePassedString(item.Date);
                item.ActionDescription = GlobalizedSentences.GetActionDescription(item.ActionTypeName);
                item.EntryTypeTooltip = Globalization.Resources.Services.EntryType.ResourceManager.GetString((item.EntryType ?? EntryTypes.User).ToString());
                item.Id = id++;
            }


            return list;
        }

        private List<NewsFeedItemModel> GroupItems(List<NewsFeedItemModel> list, bool groupAll)
        {
            var groups = from i in list
                         group i by new { i.ObjectId, i.OrganizationId, i.IsRead, i.EntryType, HasInnerList = i.InnerList != null && i.InnerList.Count > 0 } into g
                         select new { Key = g.Key, Items = g.OrderByDescending(n => n.Date) };

            var groupedList = new List<NewsFeedItemModel>();

            foreach (var gr in groups)
            {
                if (gr.Key.OrganizationId == null && gr.Key.ObjectId == null)
                {
                    var userGroups = from ii in gr.Items
                                     group ii by ii.UserObjectId into g
                                     select new { Key = g.Key, Items = g.OrderByDescending(n => n.Date) };
                    foreach (var userGr in userGroups)
                    {
                        var first = userGr.Items.First();
                        first.InnerList = userGr.Items.ToList();
                        first.InnerList.Remove(first);
                        groupedList.Add(first);
                    }
                }
                else
                {
                    //leave only one problem item
                    if (gr.Key.EntryType == EntryTypes.Problem)
                    {
                        groupedList.Add(gr.Items.First());
                    }
                    //if all news
                    else if (groupAll && !gr.Key.HasInnerList)
                    {
                        var comments = gr.Items.Where(i => CommentService.IsNewComment((ActionTypes)i.ActionTypeId));
                        groupedList.AddRange(comments);

                        var other = gr.Items.Where(i => !CommentService.IsNewComment((ActionTypes)i.ActionTypeId));

                        var first = other.FirstOrDefault();
                        if (first != null)
                        {
                            first.InnerList = other.ToList();
                            first.InnerList.Remove(first);
                            groupedList.Add(first);
                        }
                    }
                    //if interesting news
                    else
                    {
                        var items = from ii in gr.Items
                                    group ii by ii.RelatedObjectId
                                        into g
                                    select new { Key = g.Key, Items = g };

                        foreach (var item in items)
                        {
                            var itm = item.Items.FirstOrDefault();
                            if (itm != null)
                            {
                                groupedList.Add(itm);
                            }
                        }
                    }
                }
            }

            return groupedList.OrderByDescending(n => n.Date).ToList();
        }

        private List<NewsFeedItemModel> GroupViews(List<NewsFeedItemModel> list)
        {
            var groups = from i in list
                         group i by new
                         {
                             IsView = ((ActionTypes)i.ActionTypeId).In(ActionTypes.IdeaViewed, ActionTypes.IssueViewed,
                                              ActionTypes.OrganizationViewed, ActionTypes.UserProfileViewed)
                         } into g
                         select new { Key = g.Key, Items = g.OrderByDescending(n => n.Date) };
            var groupedList = new List<NewsFeedItemModel>();
            foreach (var gr in groups)
            {
                if (gr.Key.IsView)
                {
                    var first = gr.Items.First();
                    first.InnerList = gr.Items.ToList();
                    first.InnerList.Remove(first);
                    groupedList.Add(first);
                }
                else
                {
                    groupedList.AddRange(gr.Items.ToList());
                }
            }

            return groupedList.OrderByDescending(n => n.Date).ToList();
        }

        private List<NewsFeedItemModel> GroupVotes(List<NewsFeedItemModel> list)
        {
            var groups = from i in list
                         group i by new { IsVote = (i.ActionTypeId == (int)ActionTypes.Voted || i.ActionTypeId == (int)ActionTypes.IdeaVersionLiked), i.EntryType, i.UserObjectId } into g
                         select new { Key = g.Key, Items = g.OrderByDescending(n => n.Date) };
            var groupedList = new List<NewsFeedItemModel>();
            foreach (var gr in groups)
            {
                if (gr.Key.IsVote)
                {
                    var first = gr.Items.First();
                    first.InnerList = gr.Items.ToList();
                    first.InnerList.Remove(first);
                    groupedList.Add(first);
                }
                else
                {
                    groupedList.AddRange(gr.Items.ToList());
                }
            }

            return groupedList.OrderByDescending(n => n.Date).ToList();
        }

        private void GetGroupedItems(NewsFeedItemModel item, NewsFeedListViews? view)
        {
            if (item.UserCount == 1)
            {
                item.Users.Add(new UserLinkModel()
                {
                    ObjectId = item.UserObjectId,
                    FullName = item.UserFullName
                });

                item.UserCount = 0;
                return;
            }

            //if (((ActionTypes)item.ActionTypeId).In(ActionTypes.Voted, ActionTypes.LikedComment,
            //                         ActionTypes.IdeaVersionLiked, ActionTypes.IdeaCommentLiked, ActionTypes.OrganizationMemberAdded))
            //{
            //    using (var actionSession = actionSessionFactory())
            //    {
            //        var it = item;

            //        IQueryable<Data.EF.Actions.UserInterestingUser> interestedUsersQuery =
            //            new List<Data.EF.Actions.UserInterestingUser>().AsQueryable();
            //        if (CurrentUser.IsAuthenticated)
            //        {
            //            interestedUsersQuery =
            //                from lu in actionSession.GetAll<Data.EF.Actions.UserInterestingUser>()
            //                where lu.InterestedUsersId == CurrentUser.DbId
            //                select lu;
            //        }

            //        IQueryable<UserLinkModel> userQuery;
            //        if (CurrentUser.IsAuthenticated && view.HasValue && view == NewsFeedListViews.MyNews)
            //        {
            //            userQuery =
            //                from a in
            //                    actionSession.GetAll<Data.EF.Actions.Notification>().Where(
            //                        n => n.UserId == CurrentUser.DbId)
            //                    .Select(a => a.Action).Where(GetUserGroupClause(it))
            //                where (interestedUsersQuery.Where(lu => lu.InterestingUsersId == a.UserId)).Any()
            //                      && a.UserId != CurrentUser.DbId
            //                select new UserLinkModel
            //                           {
            //                               ObjectId = a.UserObjectId,
            //                               FullName = a.UserFullName
            //                           };
            //        }
            //        else
            //        {
            //            userQuery =
            //                from a in actionSession.GetAll<Data.EF.Actions.Action>().Where(GetUserGroupClause(it))
            //                where (interestedUsersQuery.Where(lu => lu.InterestingUsersId == a.UserId)).Any()
            //                select new UserLinkModel
            //                           {
            //                               ObjectId = a.UserObjectId,
            //                               FullName = a.UserFullName
            //                           };
            //        }

            //        if (item.ActionTypeId == (int)ActionTypes.OrganizationMemberAdded)
            //        {
            //            item.RelatedUsers = userQuery.Distinct().ToList();
            //            item.RelatedUserCount = it.UserCount - item.RelatedUsers.Count;
            //            item.UserCount = null;
            //        }
            //        else
            //        {
            //            item.Users = userQuery.Distinct().ToList();
            //            item.UserCount = it.UserCount - item.Users.Count;
            //            item.RelatedUserCount = null;
            //        }
            //    }
            //}
        }

        public SimpleListContainerModel GetNewsFeedGroupUsers(int pageNumber, NewsFeedListViews view, int actionTypeId, string objectId, DateTime date, string relatedObjectId, string organizationId, string text, bool isPrivate)
        {
            using (var actionSession = actionSessionFactory.CreateContext())
            {
                IQueryable<SimpleListModel> query;

                if (CurrentUser.IsAuthenticated && view == NewsFeedListViews.MyNews)
                {
                    var query1 =
                    from a in
                        actionSession.Notifications.Where(n => n.UserId == CurrentUser.DbId)
                        .Select(a => a.Action).Where(GetUserGroupClause(actionTypeId, objectId, relatedObjectId,
                                                                        organizationId, text, isPrivate))
                    where a.UserId != CurrentUser.DbId && !a.IsDeleted
                    select a;

                    if (actionTypeId == (int)ActionTypes.OrganizationMemberAdded)
                    {
                        query = from a in query1
                                group a by new { Id = a.RelatedUserObjectId, Subject = a.RelatedUserFullName }
                                    into g
                                select new SimpleListModel
                                {
                                    Id = g.Key.Id,
                                    Subject = g.Key.Subject,
                                    Date = g.Max(a => a.Date)
                                };
                    }
                    else
                    {
                        query = from a in query1
                                group a by new { Id = a.UserObjectId, Subject = a.UserFullName }
                                    into g
                                select new SimpleListModel
                                {
                                    Id = g.Key.Id,
                                    Subject = g.Key.Subject,
                                    Date = g.Max(a => a.Date)
                                };
                    }

                }
                else
                {
                    if (actionTypeId == (int)ActionTypes.OrganizationMemberAdded)
                    {
                        query =
                        from a in
                            actionSession.Actions
                            .Where(GetUserGroupClause(actionTypeId, objectId, relatedObjectId, organizationId, text, isPrivate))
                        where !a.IsDeleted
                        group a by new { Id = a.RelatedUserObjectId, Subject = a.RelatedUserFullName } into g
                        select new SimpleListModel
                        {
                            Id = g.Key.Id,
                            Subject = g.Key.Subject,
                            Date = g.Max(a => a.Date)
                        };
                    }
                    else
                    {
                        query =
                            from a in
                                actionSession.Actions
                                .Where(GetUserGroupClause(actionTypeId, objectId, relatedObjectId, organizationId, text, isPrivate))
                            where !a.IsDeleted
                            group a by new { Id = a.UserObjectId, Subject = a.UserFullName } into g
                            select new SimpleListModel
                            {
                                Id = g.Key.Id,
                                Subject = g.Key.Subject,
                                Date = g.Max(a => a.Date)
                            };
                    }
                }
                query = query.OrderByDescending(q => q.Date).GetExpandablePage(pageNumber, CustomAppSettings.PageSizeList);
                var result = query.ToList();
                result.ForEach(r => r.Type = EntryTypes.User);
                var simpleList = new SimpleListContainerModel();
                simpleList.List = new ExpandableList<SimpleListModel>(result, CustomAppSettings.PageSizeList);
                return simpleList;
            }
        }

        private Expression<Func<Data.EF.Actions.Action, bool>> GetUserGroupClause(NewsFeedItemModel it)
        {
            return GetUserGroupClause(it.ActionTypeId, it.ObjectId, it.RelatedObjectId, it.RawText, it.OrganizationId, it.IsPrivate);
        }

        private Expression<Func<Data.EF.Actions.Action, bool>> GetUserGroupClause(int actionTypeId, string objectId, string relatedObjectId, string organizationId, string text, bool isPrivate)
        {
            //var dateFrom = GetDateFrom(date);
            //var dateTo = GetDateTo(date);
            Expression<Func<Data.EF.Actions.Action, bool>> exp = a =>
                   (a.ObjectId == objectId || string.IsNullOrEmpty(a.ObjectId))
                   //&& a.Date >= dateFrom && a.Date <= dateTo
                   //&& a.Date >= dateFrom && a.Date <= dateTo
                   && a.ActionTypeId == actionTypeId
                   && (string.IsNullOrEmpty(a.RelatedObjectId) || a.RelatedObjectId == relatedObjectId)
                   && (string.IsNullOrEmpty(a.OrganizationId) || a.OrganizationId == organizationId)
                   && (string.IsNullOrEmpty(a.Text) || a.Text.StartsWith(text))
                   && a.IsPrivate == isPrivate && !a.IsDeleted;
            return exp;
        }

        private Expression<Func<Data.EF.Actions.Notification, bool>> GetUserGroupClauseForNotification(int actionTypeId, string objectId, string relatedObjectId, string organizationId, string text, bool isPrivate)
        {
            Expression<Func<Data.EF.Actions.Notification, bool>> exp = a =>
                   (a.Action.ObjectId == objectId || string.IsNullOrEmpty(a.Action.ObjectId))
                   && a.Action.ActionTypeId == actionTypeId
                   && (string.IsNullOrEmpty(a.Action.RelatedObjectId) || a.Action.RelatedObjectId == relatedObjectId)
                   && (string.IsNullOrEmpty(a.Action.OrganizationId) || a.Action.OrganizationId == organizationId)
                   && (string.IsNullOrEmpty(a.Action.Text) || a.Action.Text.StartsWith(text))
                   && a.Action.IsPrivate == isPrivate;
            return exp;
        }

        private DateTime GetDateFrom(DateTime date)
        {
            var range = 7;
            var dateDiff = (DateTime.Now - date).Days / range;
            return DateTime.Now.AddDays((-1 - dateDiff) * range); ;
        }

        private DateTime GetDateTo(DateTime date)
        {
            var range = 7;
            var dateDiff = (DateTime.Now - date).Days / range;
            return DateTime.Now.AddDays(-dateDiff * range);
        }

        private List<UrgentMessageModel> GetUnreadVersionsOfVotedIdeas(UserInfo user)
        {
            List<string> votedIdeas;
            using (var actionSession = actionSessionFactory.CreateContext(unique: true))
            {
                votedIdeas = (from a in actionSession.Actions
                              where
                                  a.UserId == user.DbId &&
                                  a.ActionTypeId == (int)ActionTypes.IdeaVersionLiked
                              select a.ObjectId).Distinct().ToList();
                using (var noSqlSession = noSqlSessionFactory())
                {
                    foreach (var ideaId in new List<string>(votedIdeas))
                    {
                        var idea =
                            noSqlSession.Find<Idea>(Query.EQ("_id", BsonObjectId.Create(ideaId))).SetFields("State")
                                .Single();
                        if (idea.IsClosed)
                        {
                            votedIdeas.Remove(ideaId);
                        }
                    }
                }
            }

            using (var votingSession = votingSessionFactory.CreateContext(unique: true))
            {
                var viewsQuery = votingSession.IdeaVersionViews.AsQueryable();
                var unreadIdeas = (from ui in votingSession.IdeaVersions
                                   where ui.UserObjectId != user.Id && votedIdeas.Contains(ui.IdeaId) &&
                                         !(from iv in viewsQuery
                                           where
                                               iv.UserDbId == user.DbId && iv.IdeaVersionObjectId == ui.VersionId
                                           select iv).Any()
                                   select new UrgentMessageModel()
                                   {
                                       ObjectId = ui.IdeaId,
                                       Subject = ui.Idea.Subject
                                   }).Distinct().ToList();
                unreadIdeas.ForEach(i => i.Type = UrgentMessageTypes.UnreadIdeaVersions);
                return unreadIdeas;
            }
        }

        private List<UrgentMessageModel> GetRandomUnreadIdeas(UserInfo user)
        {
            string[] readIdeas;
            using (var actionSession = actionSessionFactory.CreateContext(unique: true))
            {
                var q = from aa in actionSession.Actions
                        where aa.UserId == user.DbId && aa.ActionTypeId == (int)ActionTypes.IdeaViewed
                        select aa.ObjectId;
                readIdeas = q.Distinct().ToArray();
            }

            using (var noSqlSession = noSqlSessionFactory())
            {
                List<BsonObjectId> readIdeaIds = new List<BsonObjectId>();
                foreach (var id in readIdeas)
                {
                    readIdeaIds.Add(BsonObjectId.Create(id));
                }

                var unreadIdeas =
                    noSqlSession.Find<Data.MongoDB.Idea>(Query.And(Query.NotIn("_id", readIdeaIds.ToArray()),
                                                                   Query.NotIn("State",
                                                                               new[]
                                                                                   {
                                                                                           BsonValue.Create(
                                                                                               IdeaStates.Closed),
                                                                                           BsonValue.Create(
                                                                                               IdeaStates.Realized)
                                                                                   }),
                                                                   Query.EQ("IsPrivateToOrganization", false),
                                                                   Query.EQ("IsDraft", false),
                                                                   Query.In("CategoryIds", GetMyCategories(user))))
                        .SetFields("Subject", "_id").ToList();

                return GetRandomIdeas(unreadIdeas, UrgentMessageTypes.RandomIdeas);
            }
        }

        private BsonValue[] GetMyCategories(UserInfo user)
        {
            var myCategoryIds = CategoryService.GetMyCategoryIds(user);
            List<BsonValue> arr = new List<BsonValue>();
            foreach (var id in myCategoryIds)
            {
                arr.Add(BsonValue.Create(id));
            }

            return arr.ToArray();
        }

        private List<UrgentMessageModel> GetRandomUnreadIssues(UserInfo user)
        {
            string[] readIssues;
            using (var actionSession = actionSessionFactory.CreateContext(unique: true))
            {
                var q = from aa in actionSession.Actions
                        where aa.UserId == user.DbId && aa.ActionTypeId == (int)ActionTypes.IssueViewed
                        select aa.ObjectId;
                readIssues = q.Distinct().ToArray();
            }

            using (var noSqlSession = noSqlSessionFactory())
            {
                List<BsonObjectId> readIssueIds = new List<BsonObjectId>();
                foreach (var id in readIssues)
                {
                    readIssueIds.Add(BsonObjectId.Create(id));
                }

                var unreadIdeas =
                    noSqlSession.Find<Data.MongoDB.Issue>(
                        Query.And(
                            Query.NotIn("_id", readIssueIds.ToArray()),
                            Query.GT("Deadline", BsonValue.Create(DateTime.Now)),
                            Query.In("CategoryIds", GetMyCategories(user)),
                            Query.EQ("IsPrivateToOrganization", false))).SetFields("Subject", "_id").ToList();
                return GetRandomIssues(unreadIdeas);
            }
        }

        private void GetRealizedIdeas(NewsFeedIndexModel model)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                var realizedIdeas = noSqlSession.Find<Data.MongoDB.Idea>(Query.And(
                        Query.EQ("State", BsonValue.Create(IdeaStates.Realized)),
                        Query.EQ("IsPrivateToOrganization", false)))
                    .SetFields("Subject", "_id").ToList();

                model.UrgentMessages.AddRange(GetRandomIdeas(realizedIdeas, UrgentMessageTypes.RealizedIdeas));
            }
        }

        private List<UrgentMessageModel> GetMyOrganizations(UserInfo user)
        {
            List<BsonObjectId> myOrgIds = new List<BsonObjectId>();
            using (var session = actionSessionFactory.CreateContext(unique: true))
            {
                var myOrgIdStrings =
                    session.UserInterestingOrganizations.Where(
                        o => o.UserId == user.DbId && o.IsConfirmed && o.IsMember)
                        .Select(o => o.OrganizationId)
                        .ToList();
                foreach (var id in myOrgIdStrings)
                {
                    myOrgIds.Add(BsonObjectId.Create(id));
                }
            }

            using (var noSqlSession = noSqlSessionFactory())
            {
                var orgs =
                    noSqlSession.Find<Data.MongoDB.Organization>(Query.In("_id", BsonArray.Create(myOrgIds)))
                        .SetFields("_id", "Name", "Projects")
                        .ToList();
                return (from o in orgs
                        select
                            new UrgentMessageModel()
                            {
                                ObjectId = o.Id,
                                Subject = o.Name,
                                Type = UrgentMessageTypes.MyOrganizations,
                                Items =
                                        (from p in
                                             o.Projects.Where(
                                                 pr =>
                                                 pr.State == OrganizationProjectStates.New
                                                 || pr.State == OrganizationProjectStates.Planned)
                                         let myTodos =
                                             p.ToDos.Where(
                                                 td =>
                                                 (string.IsNullOrEmpty(td.ResponsibleUserId)
                                                  || td.ResponsibleUserId == user.Id)
                                                 && !td.FinishDate.HasValue)
                                         select
                                             new UrgentMessageItemModel()
                                             {
                                                 ObjectId = p.Id,
                                                 Subject = p.Subject,
                                                 Highlight =
                                                         myTodos.Where(
                                                             td =>
                                                             td.DueDate
                                                                 .HasValue
                                                             && td.DueDate
                                                             < DateTime.Now)
                                                         .Any(),
                                                 Count =
                                                         myTodos.Count() > 0
                                                             ? myTodos.Count()
                                                             : (int?)null,
                                             }).Distinct().ToList()
                            })
                    .ToList();
            }
        }

        private List<UrgentMessageModel> GetMyProjects(UserInfo user)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                var projects =
                    noSqlSession.Find<Data.MongoDB.Project>(Query.EQ("ProjectMembers.UserObjectId", user.Id))
                        .SetFields("_id", "Subject", "IdeaId", "ToDos", "MileStones")
                        .ToList();
                return (from p in projects
                        let idea =
                            noSqlSession.Find<Data.MongoDB.Idea>(Query.EQ("_id", p.IdeaId))
                            .SetFields("_id", "Subject", "State")
                            .Single()
                        where idea.ActualState == IdeaStates.Implementation
                        let myTodos =
                            p.ToDos.Where(
                                td =>
                                (string.IsNullOrEmpty(td.ResponsibleUserId) || td.ResponsibleUserId == user.Id)
                                && !td.FinishDate.HasValue).Union(
                                    from m in p.MileStones
                                    from t in m.ToDos
                                    where
                                        (string.IsNullOrEmpty(t.ResponsibleUserId) || t.ResponsibleUserId == user.Id)
                                        && !t.FinishDate.HasValue
                                    select t)
                        select
                            new UrgentMessageModel()
                            {
                                ObjectId = p.Id,
                                Subject = idea.Subject,
                                Count = myTodos.Count() > 0 ? myTodos.Count() : (int?)null,
                                Highlight =
                                        myTodos.Where(
                                            td => td.DueDate.HasValue && td.DueDate < DateTime.Now)
                                        .Any(),
                                Type = UrgentMessageTypes.MyProjects
                            }).ToList();
            }
        }

        private void GetCommentModel(IEnumerable<NewsFeedItemModel> items, UserInfo user)
        {
            {
                var filteredItems =
                    items.Where(
                        i =>
                        !string.IsNullOrEmpty(i.RelatedObjectId) && i.EntryType.HasValue
                        && CommentService.IsComment((ActionTypes)i.ActionTypeId)).ToList();
                var commentIds = filteredItems.Select(i => i.RelatedObjectId).ToList();

                var comments = CommentService.GetCommentViewFromDb(commentIds, user);
                foreach (var item in filteredItems)
                {
                    var comment = comments.Where(c => c.Id == item.RelatedObjectId).SingleOrDefault();
                    item.Comment = comment;
                }
            }
        }

        private void GetCommentModel(NewsFeedItemModel item)
        {
            var commentId = item.RelatedObjectId;

            if (!commentId.IsNullOrEmpty() && item.EntryType.HasValue && CommentService.IsComment((ActionTypes)item.ActionTypeId))
            {
                item.Comment = CommentService.GetCommentViewFromDb(commentId, null, CurrentUser);
                item.Comment.IsNewsFeed = true;
            }
        }

        private void GetProblemModel(NewsFeedItemModel item)
        {
            if (item.EntryType != EntryTypes.Problem)
            {
                return;
            }

            var model = ProblemService.GetProblemIndexItem(item.ObjectId, null, true, true);
            if (!model.Id.IsNullOrEmpty())
            {
                item.Problem = model;
            }
        }

        private void GetProblemModel(IEnumerable<NewsFeedItemModel> items, UserInfo user)
        {
            var objectIds = items.Where(i => i.EntryType == EntryTypes.Problem).Select(i => i.ObjectId).ToList();

            IEnumerable<ProblemIndexItemModel> problems;
            problems = ProblemService.GetProblemIndexItems(objectIds, user);
            foreach (var problem in problems)
            {
                var item = items.FirstOrDefault(i => i.ObjectId == problem.Id && i.Problem == null);
                if (item != null)
                {
                    item.Problem = problem;
                    //items = items.Except(items.Where(i => i.ObjectId == problem.Id && i.Problem == null));
                }
            }
        }

        public bool Delete(int userDbId, string objectId, int actionTypeId, string relatedObjectId, string text, bool isPrivate, string organizationId)
        {
            text = System.Web.HttpUtility.UrlDecode(text);
            using (var actionSession = actionSessionFactory.CreateContext(true))
            {
                if (userDbId != CurrentUser.DbId)
                {
                    var cnt = actionSession.Notifications.Delete(from a in actionSession.Notifications.Where(GetUserGroupClauseForNotification(actionTypeId, objectId, relatedObjectId, organizationId, text, isPrivate))
                                                                 where a.UserId == CurrentUser.DbId
                                                                 select a);
                    return cnt > 0;
                }
                else
                {
                    var actions = (from a in actionSession.Actions.Where(GetUserGroupClause(actionTypeId, objectId, relatedObjectId, organizationId, text, isPrivate))
                                   where a.UserId == userDbId
                                   select a);

                    var cnt = actionSession.Actions.Update(actions, a => new Data.EF.Actions.Action() { IsDeleted = true });

                    return cnt > 0;
                }
            }
        }

        public List<UrgentMessageModel> GetRandomIdeas(List<Data.MongoDB.Idea> items, UrgentMessageTypes type)
        {
            var messages = new List<UrgentMessageModel>();
            var min = Math.Min(items.Count, 2);
            for (int i = 0; i < min; i++)
            {
                var number = new Random().Next(items.Count);
                while (messages.Any(m => m.ObjectId == items[number].Id))
                {
                    number = new Random().Next(items.Count);
                }

                messages.Add(new UrgentMessageModel()
                {
                    Subject = items[number].Subject,
                    ObjectId = items[number].Id,
                    Type = type
                });
            }

            return messages;
        }

        public List<UrgentMessageModel> GetRandomIssues(List<Data.MongoDB.Issue> items)
        {
            var messages = new List<UrgentMessageModel>();
            var min = Math.Min(items.Count, 2);
            for (int i = 0; i < min; i++)
            {
                var number = new Random().Next(items.Count);
                while (messages.Any(m => m.ObjectId == items[number].Id))
                {
                    number = new Random().Next(items.Count);
                }

                messages.Add(new UrgentMessageModel()
                {
                    Subject = items[number].Subject,
                    ObjectId = items[number].Id,
                    Type = UrgentMessageTypes.RandomIssues
                });
            }

            return messages;
        }

        private List<DashboardListModel> GetActiveProjects()
        {
            using (var session = noSqlSessionFactory())
            {
                return (from i in session.GetAll<Data.MongoDB.Project>().ToList()
                        orderby i.ToDos.Count + i.MileStones.Sum(m => m.ToDos.Count()) descending
                        let idea = session.GetAll<Data.MongoDB.Idea>().Where(ii => ii.Id == i.IdeaId).Single()
                        select new DashboardListModel()
                        {
                            Subject = idea.Subject,
                            Id = i.Id
                        }).Take(2).ToList();
            }
        }

        private List<DashboardListModel> GetActiveOrganizations()
        {
            using (var session = noSqlSessionFactory())
            {
                return (from i in session.GetAll<Data.MongoDB.Organization>().ToList()
                        orderby i.Projects.Sum(p => p.ToDos.Count()) descending
                        select new DashboardListModel()
                        {
                            Subject = i.Name,
                            Id = i.Id
                        }).Take(2).ToList();
            }
        }

        public async Task<StartPageModel> GetStartPageAsync()
        {

            //var ideasTask = Task.Run(() => IdeaService.GetTopResolvedIdeas(0));
            //var issuesTask = Task.Run(() => VotingService.GetTopIssues(0));
            var ideas = IdeaService.GetTopResolvedIdeas(0);
            var issues = VotingService.GetTopIssues(0);
            var projectsTask = Task.Run(() => GetActiveProjects());
            var organizationsTask = Task.Run(() => GetActiveOrganizations());

            await Task.WhenAll(projectsTask, organizationsTask);

            return new StartPageModel()
            {
                Ideas = ideas,
                BestIssues = issues,
                ActiveProjects = projectsTask.Result,
                ActiveOrganizations = organizationsTask.Result
            };

            //model.Ideas = IdeaService.GetTopResolvedIdeas(0);
            //model.BestIssues = VotingService.GetTopIssues(0);

            //using (var session = noSqlSessionFactory())
            //{
            //   model.ActiveProjects = (from i in session.GetAll<Data.MongoDB.Project>().ToList()
            //                            orderby i.ToDos.Count + i.MileStones.Sum(m => m.ToDos.Count()) descending
            //                            let idea = session.GetAll<Data.MongoDB.Idea>().Where(ii => ii.Id == i.IdeaId).Single()
            //                            select new DashboardListModel()
            //                            {
            //                                Subject = idea.Subject,
            //                                Id = i.Id
            //                            }).Take(2).ToList();

            //    model.ActiveOrganizations = (from i in session.GetAll<Data.MongoDB.Organization>().ToList()
            //                                 orderby i.Projects.Sum(p => p.ToDos.Count()) descending
            //                                 select new DashboardListModel()
            //                                 {
            //                                     Subject = i.Name,
            //                                     Id = i.Id
            //                                 }).Take(2).ToList();
            //}

            //return model;
        }

        public StartPageModel GetStartPage()
        {
            PageSizeNewsFeed = 15;
            var model = new StartPageModel()
            {
                Ideas = IdeaService.GetTopResolvedIdeas(0),
                BestIssues = VotingService.GetTopIssues(0),
                MembersCount = UserService.GetMembersCount()
            };
            model.MembersCountString = UserService.GetMembersCountString(model.MembersCount);
            return model;
        }

        public bool CheckUser(int userId, string userName)
        {
            using (var session = noSqlSessionFactory())
            {
                var user = session.GetAll<Data.MongoDB.User>().SingleOrDefault(u => u.DbId == userId);
                if (user == null)
                {
                    return false;
                }

                if (user.UserName != userName)
                {
                    return false;
                }
            }

            return true;
        }

        public LanguageModel SetLanguage(string lang)
        {
            return UserService.ChangeUserLanguage(lang);
        }
    }
}