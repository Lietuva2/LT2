using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Transactions;
using Bus.Commands;
using Data.EF.Actions;
using Data.EF.Users;
using Data.EF.Voting;
using Data.Enums;
using Data.Infrastructure.Sessions;
using Data.MongoDB;
using Data.MongoDB.Interfaces;
using Data.ViewModels.Base;
using EntityFramework.Extensions;
using Framework.Infrastructure.Storage;
using Framework.Other;
using Framework.Strings;
using MongoDB.Bson;
using MongoDB.Driver.Builders;
using Services.Classes;
using Action = Data.EF.Actions.Action;
using Idea = Data.MongoDB.Idea;
using Notification = Data.EF.Actions.Notification;
using User = Data.MongoDB.User;

namespace Services.ModelServices
{
    public class ActionService
    {
        private readonly IActionsContextFactory actionsContextFactory;
        private readonly IUsersContextFactory usersContextFactory;
        private readonly IVotingContextFactory votingContextFactory;
        private readonly Func<INoSqlSession> noSqlSessionFactory;
        private readonly SearchService searchService;
        private readonly AddressService addressService;
        private readonly IdeaService ideaService;
        private readonly ProblemService problemService;
        private readonly VotingService votingService;
        private readonly UserService userService;

        private Data.EF.Users.User _dbUser;
        private ActionType _actionType;

        public ActionService(
            IActionsContextFactory actionsContextFactory,
            IUsersContextFactory usersSessionFactory,
            SearchService searchService,
            AddressService addressService,
            Func<INoSqlSession> noSqlSessionFactory,
            IVotingContextFactory votingSessionFactory,
            IdeaService ideaService,
            VotingService votingService,
            ProblemService problemService,
            UserService userService)
        {
            this.actionsContextFactory = actionsContextFactory;
            this.usersContextFactory = usersSessionFactory;
            this.searchService = searchService;
            this.noSqlSessionFactory = noSqlSessionFactory;
            this.addressService = addressService;
            this.votingContextFactory = votingSessionFactory;
            this.ideaService = ideaService;
            this.votingService = votingService;
            this.problemService = problemService;
            this.userService = userService;
        }

        public Action ProcessIdeaCommand(IdeaCommand command, string subject = null, string versionId = null, string commentId = null, string commentCommentId = null, string relatedUserObjectId = null)
        {
            var idea = GetIdea(command.ObjectId);

            var version = GetIdeaVersion(idea, versionId);
            var orgId = version != null ? version.OrganizationId : idea.OrganizationId;
            Action action = null;

            if (command.ActionType == ActionTypes.IdeaStateChanged)
            {
                if (idea.IsImpersonal)
                {
                    using (var session = actionsContextFactory.CreateContext(true))
                    {
                        var acts =
                            session.Actions.Where(
                                a => a.UserObjectId == command.UserId && a.ActionTypeId == (int)ActionTypes.IdeaCreated);
                        foreach (var act in acts)
                        {
                            act.IsDeleted = true;
                        }
                    }
                }

                if (idea.ActualState == IdeaStates.Resolved)
                {
                    command.SendMail = true;
                }
            }

            if (command.ActionType != ActionTypes.IdeaViewed)
            {
                ideaService.UpdateDbIdea(idea, command.UserId);
            }

            if (command.ActionType.In(ActionTypes.IdeaVersionAdded, ActionTypes.IdeaVersionEdited, ActionTypes.IdeaVersionLiked))
            {
                action = InsertAction(command.ActionType, command.MessageDate, command.UserId, idea.Id, subject ?? idea.Subject,
                                      command.Link, command.Text, orgId, command.IsPrivate, EntryTypes.Idea, idea.MunicipalityId, idea.CategoryIds,
                                      relatedUserObjectId, versionId, GetIdeaVersionSubject(idea, versionId));
            }
            else
            {
                action = InsertAction(command.ActionType, command.MessageDate, command.UserId, idea.Id, subject ?? idea.Subject,
                                      command.Link, command.Text, orgId, command.IsPrivate, EntryTypes.Idea, idea.MunicipalityId, idea.CategoryIds,
                                      relatedUserObjectId, commentId, null, null, commentCommentId);
            }

            if (command.ActionType == ActionTypes.IdeaViewed && !string.IsNullOrEmpty(versionId))
            {
                SetIdeaVersionViewed(versionId, GetUserDbId(command.UserId).Value);
            }

            GetLikedIdeaUser(idea, action, versionId, commentId, commentCommentId);

            ProcessAction(action, idea.CategoryIds.ToArray(), EntryTypes.Idea, idea.MunicipalityId);

            return action;
        }

        private void GetLikedIdeaUser(Idea idea, Action action, string versionId, string commentId, string commentCommentId)
        {
            var actionType = GetActionType(action.ActionTypeId);
            if (actionType.Reputation.HasValue)
            {
                if (action.ActionTypeId == (int)ActionTypes.IdeaVersionLiked)
                {
                    var version = idea.SummaryWiki.Versions.SingleOrDefault(v => v.Id == versionId);
                    if (version != null && action.UserObjectId != version.CreatorObjectId)
                    {
                        action.LikedUserObjectId = version.CreatorObjectId;
                        return;
                    }
                }
            }

            GetLikedCommentUser(idea, action, commentId, commentCommentId);
        }

        private void GetLikedProblemUser(Data.MongoDB.Problem problem, Action action, string commentId)
        {
            var actionType = GetActionType(action.ActionTypeId);
            if (actionType.Reputation.HasValue)
            {
                if (action.ActionTypeId == (int)ActionTypes.ProblemVoted || action.ActionTypeId == (int)ActionTypes.ProblemDevoted)
                {
                    if (action.UserObjectId != problem.UserObjectId)
                    {
                        action.LikedUserObjectId = problem.UserObjectId;
                        return;
                    }
                }
            }

            GetLikedCommentUser(problem, action, commentId, null);
        }

        private void GetLikedCommentUser(ICommentable entity, Action action, string commentId, string commentCommentId)
        {
            var comment = entity.Comments.SingleOrDefault(c => c.Id == commentId);
            if (comment != null)
            {
                if (action.ActionTypeId == (int)ActionTypes.CommentCommentLiked)
                {

                    var commentComment = comment.Comments.SingleOrDefault(c => c.Id == commentCommentId);
                    if (commentComment != null && commentComment.UserObjectId != action.UserObjectId)
                    {
                        action.LikedUserObjectId = commentComment.UserObjectId;
                        return;
                    }
                }
                if (comment.UserObjectId != action.UserObjectId)
                {
                    action.LikedUserObjectId = comment.UserObjectId;
                }
            }
        }

        public Action ProcessIssueCommand(ActionCommand command, string commentId = null, string commentCommentId = null, string relatedUserObjectId = null)
        {
            var issue = GetIssue(command.ObjectId);

            if (command.ActionType != ActionTypes.IssueViewed)
            {
                votingService.UpdateDbIssue(issue);
            }

            var action = InsertAction(command.ActionType, command.MessageDate, command.UserId, issue.Id, issue.Subject,
                          command.Link, command.Text, issue.OrganizationId, command.IsPrivate, EntryTypes.Issue, issue.MunicipalityId, issue.CategoryIds, relatedUserObjectId, commentId, null, null, commentCommentId);

            var actionType = GetActionType(action.ActionTypeId);
            if (actionType.Reputation.HasValue)
            {
                GetLikedCommentUser(issue, action, commentId, commentCommentId);
            }

            ProcessAction(action, issue.CategoryIds.ToArray(), EntryTypes.Issue, issue.MunicipalityId);

            return action;
        }

        public Action ProcessProblemCommand(ActionCommand command, string relatedObjectId, string relatedUserId, string relatedSubject, string relatedLink)
        {
            var problem = GetProblem(command.ObjectId);

            problemService.UpdateDbProblem(problem);

            var action = InsertAction(command.ActionType, command.MessageDate, command.UserId, problem.Id, problem.Text.LimitLength(198),
                          command.Link, command.Text, problem.OrganizationId, command.IsPrivate, EntryTypes.Problem, problem.MunicipalityId, problem.CategoryIds, relatedUserId, relatedObjectId, relatedSubject, relatedLink);

            GetLikedProblemUser(problem, action, relatedObjectId);

            ProcessAction(action, problem.CategoryIds.ToArray(), EntryTypes.Problem, problem.MunicipalityId);

            return action;
        }

        public Action ProcessProjectCommand(ActionCommand command, string mileStoneId, string toDoId, string toDoLink, string commentId = null)
        {
            var project = GetProject(command.ObjectId);
            var idea = GetIdea(project.IdeaId);
            var todo = GetToDo(project, mileStoneId, toDoId);
            var action = InsertAction(command.ActionType, command.MessageDate, command.UserId, command.ObjectId, idea.Subject,
                          command.Link, command.Text, idea.OrganizationId, command.IsPrivate, EntryTypes.Project, idea.MunicipalityId, idea.CategoryIds,
                          todo != null ? todo.ResponsibleUserId : null, toDoId, todo != null ? todo.Subject : null, toDoLink, commentId);

            ProcessAction(action, idea.CategoryIds.ToArray(), EntryTypes.Project);

            return action;
        }

        public Action ProcessProjectMemberCommand(ActionCommand command, int relatedUserDbId)
        {
            var project = GetProject(command.ObjectId);
            var idea = GetIdea(project.IdeaId);
            var action = InsertAction(command.ActionType, command.MessageDate, command.UserId, command.ObjectId, idea.Subject,
                          command.Link, command.Text, idea.OrganizationId, command.IsPrivate, EntryTypes.Project, idea.MunicipalityId, idea.CategoryIds, GetUserObjectId(relatedUserDbId));

            ProcessAction(action, idea.CategoryIds.ToArray(), EntryTypes.Project);

            return action;
        }

        public Action ProcessOrganizationCommand(ActionCommand command, int? relatedUserId = null)
        {
            var action = InsertAction(command.ActionType, command.MessageDate, command.UserId, command.ObjectId, GetOrganizationName(command.ObjectId),
                          command.Link, command.Text, command.ObjectId, command.IsPrivate, EntryTypes.Organization, null, null, GetUserObjectId(relatedUserId));

            ProcessAction(action, null, EntryTypes.Organization);

            return action;
        }

        public Action ProcessOrganizationProjectCommand(ActionCommand command, string projectId, string toDoId, string relatedLink, string commentId = null)
        {
            var project = GetOrganizationProject(command.ObjectId, projectId);
            var todo = project.ToDos.SingleOrDefault(t => t.Id == toDoId) ?? new ToDo();
            var action = InsertAction(command.ActionType, command.MessageDate, command.UserId, projectId, project.Subject,
                command.Link, command.Text, command.ObjectId, command.IsPrivate, EntryTypes.Organization, null, null, todo.ResponsibleUserId, toDoId, todo.Subject, relatedLink, commentId);

            ProcessAction(action, null, EntryTypes.Organization);

            return action;
        }

        public Action ProcessUserCommand(ActionCommand command, string relatedUserId = null, string commentId = null, string commentCommentId = null)
        {
            if (command.ActionType != ActionTypes.UserProfileViewed && !string.IsNullOrEmpty(command.ObjectId))
            {
                userService.UpdateDbUser(command.ObjectId);
            }

            var action = InsertAction(command.ActionType, command.MessageDate, command.UserId, command.ObjectId, GetUserFullName(command.ObjectId),
                          command.Link, command.Text, command.ObjectId, command.IsPrivate, EntryTypes.User, null, null, relatedUserId, commentId, null, null, commentCommentId);

            var actionType = GetActionType(action.ActionTypeId);
            if (actionType.Reputation.HasValue)
            {
                var user = GetUser(command.ObjectId);
                GetLikedCommentUser(user, action, commentId, commentCommentId);
            }

            ProcessAction(action, null, EntryTypes.User);

            return action;
        }

        public void UpdateUserReputations()
        {
            using (var session = actionsContextFactory.CreateContext(true))
            {
                var actions = session.Actions.Where(a => a.ActionType.Reputation.HasValue).ToList();
                foreach (var action in actions)
                {
                    if (action.EntryTypeId == (int)EntryTypes.Idea)
                    {
                        var idea = GetIdea(action.ObjectId);
                        GetLikedIdeaUser(idea, action, action.RelatedObjectId, action.RelatedObjectId, action.RelatedRelatedObjectId);
                    }
                    if (action.EntryTypeId == (int)EntryTypes.Problem)
                    {
                        var problem = GetProblem(action.ObjectId);
                        GetLikedProblemUser(problem, action, action.RelatedObjectId);
                    }
                    if (action.EntryTypeId == (int)EntryTypes.Issue)
                    {
                        var issue = GetIssue(action.ObjectId);
                        GetLikedCommentUser(issue, action, action.RelatedObjectId, action.RelatedRelatedObjectId);
                    }
                    if (action.EntryTypeId == (int)EntryTypes.User)
                    {
                        var user = GetUser(action.ObjectId);
                        GetLikedCommentUser(user, action, action.RelatedObjectId, action.RelatedRelatedObjectId);
                    }
                }
            }
        }

        public Action ProcessCategoryAction(ActionTypes actionType, DateTime date, int userId, List<short> categoryIds)
        {
            var action = InsertAction(actionType, date, GetUserObjectId(userId), userId.ToString(), null, null, null, null, false, EntryTypes.User, null, categoryIds.Select(c => (int)c).ToList());
            using (var actionSession = actionsContextFactory.CreateContext(true))
            {
                actionSession.Actions.Add(action);
            }

            return action;
        }

        public void SetIdeaVersionViewed(string versionId, int userDbId)
        {
            using (var votingSession = votingContextFactory.CreateContext(true))
            {
                if (votingSession.IdeaVersionViews.Where(v => v.IdeaVersionObjectId == versionId && v.UserDbId == userDbId).Any())
                {
                    return;
                }

                var view = new Data.EF.Voting.IdeaVersionView()
                {
                    IdeaVersionObjectId = versionId,
                    UserDbId = userDbId
                };
                votingSession.IdeaVersionViews.Add(view);
            }
        }

        public void SetNotificationViewed(NotificationViewedCommand command)
        {
            using (var actionSession = actionsContextFactory.CreateContext(true))
            {
                var notifications = (from a in actionSession.Notifications
                                     where (a.UserId == command.NotifyUserDbId && a.IsRead == false && a.ActionId >= command.LastViewedId)
                                     select a).ToList();

                foreach (var n in notifications)
                {
                    n.IsRead = true;
                }
            }
        }

        public void DeleteVoteAction(string objectId, string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return;
            }

            using (var session = actionsContextFactory.CreateContext(true))
            {
                var actions =
                    session.Actions.Where(
                        a =>
                        a.ObjectId == objectId && a.UserObjectId == userId
                        && (a.ActionTypeId == (int)ActionTypes.Voted
                            || a.ActionTypeId == (int)ActionTypes.IdeaVersionLiked)).ToList();

                foreach (var action in actions)
                {
                    action.IsDeleted = true;
                }
            }
        }

        public void DeleteToDo(string toDoId)
        {
            using (var actionSession = actionsContextFactory.CreateContext(true))
            {
                var yesterday = DateTime.Now.AddDays(-1);
                var actions = actionSession.Actions.Where(a => (
                    a.ActionTypeId == (int)ActionTypes.ToDoAdded ||
                    a.ActionTypeId == (int)ActionTypes.ToDoEdited ||
                    a.ActionTypeId == (int)ActionTypes.OrganizationToDoAdded ||
                    a.ActionTypeId == (int)ActionTypes.OrganizationToDoEdited)
                    && a.RelatedObjectId == toDoId && a.Date > yesterday).ToList();
                foreach (var action in actions)
                {
                    action.IsDeleted = true;
                }
            }
        }

        public void UnfinishToDo(string toDoId)
        {
            using (var actionSession = actionsContextFactory.CreateContext(true))
            {
                var actions =
                    actionSession.Actions.Where(
                        a => (
                            a.ActionTypeId == (int)ActionTypes.ToDoFinished ||
                            a.ActionTypeId == (int)ActionTypes.OrganizationToDoFinished) &&
                            a.RelatedObjectId == toDoId).ToList();
                foreach (var action in actions)
                {
                    action.IsDeleted = true;
                }
            }
        }

        private void ProcessAction(Action action, int[] categoryIds, EntryTypes type, int? municipalityId = null)
        {
            using (var actionSession = actionsContextFactory.CreateContext())
            {
                SetGroupedActions(action);
                actionSession.Actions.Add(action);
                InsertNotifications(action, categoryIds, municipalityId);
                try
                {
                    actionSession.SaveChanges();
                }
                catch (Exception ex)
                {
                    throw;
                }
            }

            if (!action.IsPrivate && !((ActionTypes)action.ActionTypeId).In(ActionTypes.IdeaViewed, ActionTypes.IssueViewed, ActionTypes.OrganizationViewed, ActionTypes.UserProfileViewed))
            {
                if (type == EntryTypes.User)
                {
                    searchService.UpdateIndex(action.UserObjectId, type);
                }
                else if (type == EntryTypes.Organization)
                {
                    searchService.UpdateIndex(action.OrganizationId, type);
                }
                else
                {
                    searchService.UpdateIndex(action.ObjectId, type);
                }
            }

            AddUserPoints(action.ActionTypeId, action.UserId, categoryIds);
        }

        private void SetGroupedActions(Action action)
        {
            using (var actionSession = actionsContextFactory.CreateContext())
            {
                var relatedActions = new List<Action>();
                if (CommentService.IsComment((ActionTypes) action.ActionTypeId))
                {
                    var query =
                        actionSession.Actions.Where(
                            a => a.ObjectId == action.ObjectId).AsQueryable();
                    if (!string.IsNullOrEmpty(action.RelatedObjectId))
                    {
                        query = query.Where(a => a.RelatedObjectId == action.RelatedObjectId);
                    }
                    else
                    {
                        query = query.Where(a => string.IsNullOrEmpty(a.RelatedObjectId));
                    }

                    relatedActions = query.ToList();
                }
                else
                {
                    var query =
                        actionSession.Actions.Where(
                            a => a.ActionTypeId == action.ActionTypeId && a.UserId == action.UserId && !a.IsGrouped).AsQueryable();
                    if (!string.IsNullOrEmpty(action.ObjectId))
                    {
                        query = query.Where(a => a.ObjectId == action.ObjectId);
                    }
                    else
                    {
                        query = query.Where(a => string.IsNullOrEmpty(a.ObjectId));
                    }

                    if (!string.IsNullOrEmpty(action.RelatedObjectId))
                    {
                        query = query.Where(a => a.RelatedObjectId == action.RelatedObjectId);
                    }
                    else
                    {
                        query = query.Where(a => string.IsNullOrEmpty(a.RelatedObjectId));
                    }

                    if (!string.IsNullOrEmpty(action.RelatedRelatedObjectId))
                    {
                        query = query.Where(a => a.RelatedRelatedObjectId == action.RelatedRelatedObjectId);
                    }
                    else
                    {
                        query = query.Where(a => string.IsNullOrEmpty(a.RelatedRelatedObjectId));
                    }

                    relatedActions = query.ToList();
                }

                int? groupId = relatedActions.Any() ? relatedActions.Min(a => a.Id) : (int?)null;

                foreach (var relatedAction in relatedActions)
                {
                    relatedAction.IsGrouped = true;
                    relatedAction.GroupId = groupId;
                }

                action.IsGrouped = false;
                action.GroupId = groupId;
            }
        }

        private Action InsertAction(ActionTypes actionType, DateTime date, string userId, string objectId, string subject, string link, string text, string organizationId, bool isPrivate, EntryTypes entryType, int? municipalityId, List<int> categoryIds, string relatedUserObjectId = null, string relatedObjectId = null, string relatedSubject = null, string relatedLink = null, string relatedRelatedObjectId = null)
        {
            var action = new Action
            {
                ActionTypeId = (short)actionType,
                ActionTypeName = actionType.ToString(),
                Date = date,
                UserId = GetUserDbId(userId).Value,
                UserObjectId = userId,
                UserFullName = GetUserFullName(userId),
                ObjectId = objectId,
                Subject = subject,
                Link = Uri.IsWellFormedUriString(link, UriKind.Absolute) ? new Uri(link).PathAndQuery : link,
                RelatedSubject = relatedSubject,
                RelatedLink = Uri.IsWellFormedUriString(relatedLink, UriKind.Absolute) ? new Uri(relatedLink).PathAndQuery : relatedLink,
                Text = text,
                RelatedObjectId = relatedObjectId,
                OrganizationId = organizationId,
                OrganizationName = GetOrganizationName(organizationId),
                IsPrivate = isPrivate,
                RelatedRelatedObjectId = relatedRelatedObjectId,
                RelatedUserObjectId = relatedUserObjectId,
                RelatedUserFullName = GetUserFullName(relatedUserObjectId),
                EntryTypeId = (short)entryType,
                MunicipalityId = municipalityId
            };

            if (categoryIds != null)
            {
                foreach (var categoryId in categoryIds)
                {
                    action.ActionCategories.Add(new ActionCategory()
                    {
                        CategoryId = (short)categoryId
                    });
                }
            }

            return action;
        }

        public void DeleteCommentActions(string commentId, EntryTypes type, string userObjectId)
        {
            string objectId = null;
            using (var actionSession = actionsContextFactory.CreateContext(true))
            {
                var ans =
                    actionSession.Actions.Where(
                        a => a.RelatedRelatedObjectId == commentId || a.RelatedObjectId == commentId).ToList();
                objectId = ans.Select(a => a.ObjectId).Distinct().FirstOrDefault();
                foreach (var an in ans)
                {
                    an.IsDeleted = true;
                }

                actionSession.SaveChanges();

                var lastAction = actionSession.Actions.Where(
                        a => (a.RelatedRelatedObjectId == commentId || a.RelatedObjectId == commentId) && !a.IsDeleted).OrderByDescending(a => a.Id).FirstOrDefault();
                if (lastAction != null)
                {
                    SetGroupedActions(lastAction);
                }
            }

            if (!string.IsNullOrEmpty(objectId))
            {
                searchService.UpdateIndex(objectId, type);
                UpdateDbObject(objectId, type, userObjectId);
            }
        }

        public void UnlikeComment(CommentUnlikedCommand command)
        {
            string objectId = null;
            using (var actionSession = actionsContextFactory.CreateContext(true))
            {
                var ans =
                    actionSession.Actions.Where(
                        a => ((a.ActionTypeId == (int)ActionTypes.LikedComment || a.ActionTypeId == (int)ActionTypes.IdeaCommentLiked || a.ActionTypeId == (int)ActionTypes.ProblemCommentLiked)
                            && a.RelatedObjectId == command.CommentId) ||
                            (a.ActionTypeId == (int)ActionTypes.CommentCommentLiked
                            && a.RelatedRelatedObjectId == command.CommentId
                            ) && !a.IsDeleted).ToList();
                objectId = ans.Select(a => a.ObjectId).Distinct().FirstOrDefault();
                foreach (var an in ans)
                {
                    an.IsDeleted = true;
                }

                actionSession.SaveChanges();

                var lastAction = actionSession.Actions.Where(
                        a => (a.RelatedRelatedObjectId == command.CommentId || a.RelatedObjectId == command.CommentId) && !a.IsDeleted).OrderByDescending(a => a.Id).FirstOrDefault();
                if (lastAction != null)
                {
                    SetGroupedActions(lastAction);
                }
            }

            if (!string.IsNullOrEmpty(objectId))
            {
                searchService.UpdateIndex(objectId, command.EntryType);
                UpdateDbObject(objectId, command.EntryType, command.UserObjectId);
            }
        }

        private void UpdateDbObject(string objectId, EntryTypes type, string userObjectId = null)
        {
            if (type == EntryTypes.Issue)
            {
                votingService.UpdateDbIssue(objectId);
            }

            if (type == EntryTypes.Idea)
            {
                ideaService.UpdateDbIdea(objectId, userObjectId);
            }

            if (type == EntryTypes.Problem)
            {
                problemService.UpdateDbProblem(objectId);
            }

            if (type == EntryTypes.User)
            {
                userService.UpdateDbUser(objectId);
            }
        }

        private void InsertNotifications(Action action, int[] categoryIds, int? municipalityId = null)
        {
            if (((ActionTypes)action.ActionTypeId).In(ActionTypes.IssueViewed, ActionTypes.IdeaViewed,
                                       ActionTypes.UserProfileViewed, ActionTypes.OrganizationViewed))
            {
                return;
            }

            var usersToNotify = new List<int>();
            List<Subscribtion> mainSubscribtions = new List<Subscribtion>();
            List<Subscribtion> commentSubscribtions = new List<Subscribtion>();


            if (action.IsPrivate && !string.IsNullOrEmpty(action.OrganizationId) ||
                action.ActionTypeId == (short)ActionTypes.OrganizationJoining)
            {
                usersToNotify.AddRange(NotifyOrganizationMembers(action.OrganizationId));
            }
            else
            {
                using (var session = actionsContextFactory.CreateContext())
                {
                    var subscribtions = session.Subscribtions.Where(
                        s => s.ObjectId == action.ObjectId || s.ObjectId == action.RelatedObjectId).ToList();
                    mainSubscribtions = subscribtions.Where(s => s.ObjectId == action.ObjectId).ToList();
                    commentSubscribtions = subscribtions.Where(s => s.ObjectId == action.RelatedObjectId).ToList();
                }

                var authorId = NotifyAuthor(action.ObjectId, (EntryTypes)action.EntryTypeId);
                if (authorId.HasValue)
                {
                    usersToNotify.Add(authorId.Value);
                }
                usersToNotify.AddRange(NotifyInterestedUsers(action.UserId));
                usersToNotify.AddRange(NotifyOrganizationMembers(action.OrganizationId));
                usersToNotify.AddRange(NotifyOrganizationUsers(action.OrganizationId));
                if (!((ActionTypes) action.ActionTypeId).In(ActionTypes.LikedComment, ActionTypes.CommentCommentLiked,
                                                            ActionTypes.IdeaCommentLiked, ActionTypes.IdeaVersionLiked,
                                                            ActionTypes.ProblemCommentLiked, ActionTypes.ProblemVoted, 
                                                            ActionTypes.ProblemDevoted, ActionTypes.Voted))
                {

                    usersToNotify.AddRange(NotifyObjectUsers(action.UserId, action.ObjectId));
                    usersToNotify.AddRange(NotifyRelatedObjectUsers(action.UserId, action.ObjectId));

                    usersToNotify.AddRange(commentSubscribtions.Where(s => s.Subscribed).Select(s => s.UserId).ToList());
                    usersToNotify.AddRange(mainSubscribtions.Where(s => s.Subscribed && !commentSubscribtions.Where(c => c.Subscribed == false).Select(c => c.UserId).Contains(s.UserId)).Select(s => s.UserId).ToList());
                }
                
                usersToNotify.AddRange(NotifyRelatedCommentAuthors(action.RelatedUserObjectId));

                if (action.EntryTypeId == (short)EntryTypes.User && action.UserObjectId != action.ObjectId)
                {
                    var dbId = GetUserDbId(action.ObjectId);
                    if (dbId.HasValue)
                    {
                        usersToNotify.Add(dbId.Value);
                    }
                }

                if (((ActionTypes)action.ActionTypeId).In(ActionTypes.IssueCreated, ActionTypes.IdeaCreated, ActionTypes.ProblemCreated, ActionTypes.IdeaVersionAdded))
                {
                    if (categoryIds != null && categoryIds.Length > 0)
                    {
                        usersToNotify.AddRange(NotifyCategoryUsers(categoryIds));
                    }
                    if (municipalityId.HasValue)
                    {
                        usersToNotify.AddRange(NotifyMunicipalityUsers(municipalityId));
                    }
                }

                if (((ActionTypes)action.ActionTypeId).In(ActionTypes.IdeaCreated, ActionTypes.IdeaEdited))
                {
                    usersToNotify.AddRange(NotifyRelatedIdeaUsers(action.ObjectId));
                }

                if (((ActionTypes)action.ActionTypeId).In(ActionTypes.IssueCreated, ActionTypes.IssueEdited))
                {
                    usersToNotify.AddRange(NotifyIssueRelatedUsers(action.ObjectId));
                }

                if (action.EntryTypeId == (short)EntryTypes.Project)
                {
                    usersToNotify.AddRange(NotifyProjectMembers(action.ObjectId));
                }
            }

            if (action.ActionTypeId != (short)ActionTypes.OrganizationJoined)
            {
                usersToNotify = usersToNotify.Where(u => u != action.UserId).ToList();
            }

            usersToNotify = usersToNotify.Except(commentSubscribtions.Where(s => s.Subscribed == false).Select(s => s.UserId)).ToList();
            usersToNotify = usersToNotify.Except(mainSubscribtions.Where(s => s.Subscribed == false && !commentSubscribtions.Where(c => c.Subscribed).Select(c => c.UserId).Contains(s.UserId)).Select(s => s.UserId)).ToList();

            usersToNotify = usersToNotify.Distinct().ToList();

            foreach (var interestedUserId in usersToNotify)
            {
                action.Notifications.Add(new Notification
                                             {
                                                 Action = action,
                                                 IsRead = false,
                                                 UserId = interestedUserId
                                             });
            }

            if (((ActionTypes) action.ActionTypeId).In(ActionTypes.ProblemCreated))
            {
                action.Notifications.Add(new Notification
                {
                    Action = action,
                    IsRead = true,
                    UserId = action.UserId
                });
            }
        }

        private int? NotifyAuthor(string objectId, EntryTypes type)
        {
            using (var session = noSqlSessionFactory())
            {
                if (type == EntryTypes.Idea)
                {
                    var idea = session.GetById<Data.MongoDB.Idea>(objectId);
                    if (idea != null)
                    {
                        return userService.GetUserDbId(idea.UserObjectId);
                    }
                }
                else if (type == EntryTypes.Issue)
                {
                    var issue = session.GetById<Data.MongoDB.Issue>(objectId);
                    if (issue != null)
                    {
                        return userService.GetUserDbId(issue.UserObjectId);
                    }
                }
                else if (type == EntryTypes.Problem)
                {
                    var problem = session.GetById<Data.MongoDB.Problem>(objectId);
                    if (problem != null)
                    {
                        return userService.GetUserDbId(problem.UserObjectId);
                    }
                }
            }

            return null;
        }

        private void AddUserPoints(short actionTypeId, int userId, int[] categoryIds)
        {
            var points = GetActionPoints(actionTypeId);

            if (points == 0)
            {
                return;
            }

            using (var userSession = usersContextFactory.CreateContext(true))
            {
                if (categoryIds != null && categoryIds.Length > 0)
                {
                    AddPoints(userSession, userId, categoryIds.First(), points);
                }
                else
                {
                    AddPoints(userSession, userId, null, points);
                }
            }
        }

        private void AddPoints(UsersEntities userSession, int userId, int? categoryId, int points)
        {
            var ucQuery = userSession.UserCategories.Where(c => c.UserId == userId);
            if (categoryId.HasValue)
            {
                ucQuery = ucQuery.Where(c => c.CategoryId == categoryId);
            }
            else
            {
                ucQuery = ucQuery.Where(c => !c.CategoryId.HasValue);
            }

            var uc = ucQuery.SingleOrDefault();

            if (uc == null)
            {
                uc = new UserCategory()
                {
                    CategoryId = categoryId,
                    UserId = userId,
                    IsExpert = false,
                    Points = 0
                };

                userSession.UserCategories.Add(uc);
            }

            uc.Points += points;
        }

        private string GetOrganizationName(string id)
        {
            using (var session = noSqlSessionFactory())
            {
                return session.GetAll<Organization>().Where(o => o.Id == id).Select(o => o.Name).SingleOrDefault();
            }
        }

        private Data.MongoDB.User GetUser(string id)
        {
            using (var session = noSqlSessionFactory())
            {
                return session.GetSingle<Data.MongoDB.User>(i => i.Id == id);
            }
        }

        private int? GetUserDbId(string id)
        {
            if (GetDbUser(id) == null)
            {
                return null;
            }

            return GetDbUser(id).Id;
        }

        private string GetUserObjectId(int? dbId)
        {
            if (!dbId.HasValue)
            {
                return null;
            }

            using (var session = usersContextFactory.CreateContext())
            {
                return session.Users.Where(u => u.Id == dbId).Select(u => u.ObjectId).SingleOrDefault();
            }
        }

        private string GetUserFullName(string id)
        {
            var user = GetDbUser(id);

            if (user == null)
            {
                return null;
            }


            return user.FirstName + " " + user.LastName;
        }

        private Data.EF.Users.User GetDbUser(string id)
        {
            if (_dbUser == null || _dbUser.ObjectId != id)
            {
                using (var session = usersContextFactory.CreateContext())
                {
                    _dbUser = session.Users.SingleOrDefault(u => u.ObjectId == id);
                }
            }

            return _dbUser;
        }

        private string GetActionTypeName(short actionTypeId)
        {
            return GetActionType(actionTypeId).Name;
        }

        private int GetActionPoints(short actionTypeId)
        {
            return GetActionType(actionTypeId).Points;
        }

        private ActionType GetActionType(short actionTypeId)
        {
            if (_actionType == null || _actionType.Id != actionTypeId)
            {
                using (var session = actionsContextFactory.CreateContext(true))
                {
                    _actionType = session.ActionTypes.SingleOrDefault(u => u.Id == actionTypeId);
                }
            }

            return _actionType;
        }

        private List<int> NotifyInterestedUsers(int userId)
        {
            using (var actionSession = actionsContextFactory.CreateContext())
            {
                return (from u in actionSession.UserInterestingUsers
                        where u.InterestingUsersId == userId
                        select u.InterestedUsersId).ToList();
            }
        }

        private List<int> NotifyOrganizationUsers(string organizationId)
        {
            if (!string.IsNullOrEmpty(organizationId))
            {
                using (var actionSession = actionsContextFactory.CreateContext())
                {
                    return (from u in actionSession.UserInterestingOrganizations
                            where u.OrganizationId == organizationId
                            select u.UserId).ToList();
                }
            }

            return new List<int>();
        }

        private List<int> NotifyOrganizationMembers(string organizationId)
        {
            if (!string.IsNullOrEmpty(organizationId))
            {
                using (var actionSession = actionsContextFactory.CreateContext())
                {
                    return (from u in actionSession.UserInterestingOrganizations
                            where
                                u.OrganizationId == organizationId && u.IsMember &&
                                u.IsConfirmed
                            select u.UserId).ToList();
                }
            }

            return new List<int>();
        }

        private List<int> NotifyCategoryUsers(int[] categoryIds)
        {
            using (var actionSession = actionsContextFactory.CreateContext())
            {
                return (from c in actionSession.InterestingCategories
                        where categoryIds.Contains(c.CategoryId)
                        select c.UserId).ToList();
            }
        }

        private List<int> NotifyObjectUsers(int userId, string objectId)
        {
            if (string.IsNullOrEmpty(objectId))
            {
                return new List<int>();
            }

            using (var actionSession = actionsContextFactory.CreateContext())
            {
                return (from a in actionSession.Actions
                        where
                            a.UserId != userId && a.ObjectId == objectId &&
                            a.ActionTypeId != (short)ActionTypes.IssueViewed &&
                            a.ActionTypeId != (short)ActionTypes.IdeaViewed &&
                            a.ActionTypeId != (short)ActionTypes.UserProfileViewed &&
                            a.ActionTypeId != (short)ActionTypes.OrganizationViewed &&
                            (a.ActionTypeId != (short)ActionTypes.CommentCommentLiked &&
                            a.ActionTypeId != (short)ActionTypes.IdeaCommentLiked &&
                            a.ActionTypeId != (short)ActionTypes.UserCommentLiked)
                        select a.UserId).ToList();
            }
        }

        private List<int> NotifyRelatedCommentAuthors(string relatedUserObjectId)
        {
            if (string.IsNullOrEmpty(relatedUserObjectId))
            {
                return new List<int>();
            }

            using (var userSession = usersContextFactory.CreateContext())
            {
                return userSession.Users.Where(u => u.ObjectId == relatedUserObjectId).Select(u => u.Id).ToList();
            }
        }

        private List<int> NotifyRelatedIdeaUsers(string ideaId)
        {
            using (var session = votingContextFactory.CreateContext())
            {
                var usersToNotify = new List<int?>();
                var problems = (from pi in session.ProblemIdeas
                                where pi.IdeaId == ideaId
                                select pi.Problem).ToList();
                foreach (var problem in problems)
                {
                    usersToNotify.Add(GetUserDbId(problem.UserObjectId));
                    usersToNotify.AddRange(problem.ProblemSupporters.Select(p => p.UserId).ToList().Select(GetUserDbId));
                    usersToNotify.AddRange(problem.ProblemComments.Select(p => p.Comment.UserObjectId).ToList().Select(GetUserDbId));
                    usersToNotify.AddRange(problem.ProblemComments.SelectMany(p => p.Comment.Comments1).Select(c => c.UserObjectId).ToList().Select(GetUserDbId));
                }

                var relatedIdeas = (from pi in session.RelatedIdeas
                                    where pi.IdeaObjectId == ideaId || pi.RelatedIdeaObjectId == ideaId
                                    select pi.Idea.Id == ideaId ? pi.Idea1 : pi.Idea).ToList();
                foreach (var relatedIdea in relatedIdeas)
                {
                    usersToNotify.Add(GetUserDbId(relatedIdea.UserObjectId));
                    usersToNotify.AddRange(relatedIdea.IdeaVotes.Select(p => p.UserObjectId).ToList().Select(GetUserDbId));
                    usersToNotify.AddRange(relatedIdea.IdeaComments.Select(p => p.Comment.UserObjectId).ToList().Select(GetUserDbId));
                    usersToNotify.AddRange(relatedIdea.IdeaComments.SelectMany(p => p.Comment.Comments1).Select(c => c.UserObjectId).ToList().Select(GetUserDbId));
                }

                return usersToNotify.Where(u => u.HasValue).Select(u => u.Value).ToList();
            }
        }

        private List<int> NotifyIssueRelatedUsers(string issueId)
        {
            using (var session = votingContextFactory.CreateContext())
            {
                var usersToNotify = new List<int?>();
                var problems = (from pi in session.ProblemIssues
                                where pi.IssueId == issueId
                                select pi.Problem).ToList();
                foreach (var problem in problems)
                {
                    usersToNotify.Add(GetUserDbId(problem.UserObjectId));
                    usersToNotify.AddRange(problem.ProblemSupporters.Select(p => p.UserId).ToList().Select(GetUserDbId));
                    usersToNotify.AddRange(problem.ProblemComments.Select(p => p.Comment.UserObjectId).ToList().Select(GetUserDbId));
                    usersToNotify.AddRange(problem.ProblemComments.SelectMany(p => p.Comment.Comments1).Select(c => c.UserObjectId).ToList().Select(GetUserDbId));
                }

                var relatedIdeas = (from pi in session.IdeaIssues
                                    where pi.IssueObjectId == issueId
                                    select pi.Idea).ToList();
                foreach (var relatedIdea in relatedIdeas)
                {
                    usersToNotify.Add(GetUserDbId(relatedIdea.UserObjectId));
                    usersToNotify.AddRange(relatedIdea.IdeaVotes.Select(p => p.UserObjectId).ToList().Select(GetUserDbId));
                    usersToNotify.AddRange(relatedIdea.IdeaComments.Select(p => p.Comment.UserObjectId).ToList().Select(GetUserDbId));
                    usersToNotify.AddRange(relatedIdea.IdeaComments.SelectMany(p => p.Comment.Comments1).Select(c => c.UserObjectId).ToList().Select(GetUserDbId));
                }

                return usersToNotify.Where(u => u.HasValue).Select(u => u.Value).ToList();
            }
        }

        private List<int> NotifyRelatedObjectUsers(int userId, string objectId)
        {
            List<string> relatedObjectIds;
            if (string.IsNullOrEmpty(objectId))
            {
                return new List<int>();
            }

            using (var votingSession = votingContextFactory.CreateContext())
            {
                relatedObjectIds =
                    votingSession.IdeaIssues.Where(i => i.IssueObjectId == objectId).Select(
                        i => i.IdeaObjectId).ToList();

                relatedObjectIds.AddRange(votingSession.RelatedIdeas.Where(i => i.IdeaObjectId == objectId || i.RelatedIdeaObjectId == objectId).Select(
                        cId => cId.IdeaObjectId == objectId ? cId.RelatedIdeaObjectId : cId.IdeaObjectId).ToList());
            }

            List<int> userIds = new List<int>();
            foreach (var id in relatedObjectIds.Distinct())
            {
                userIds.AddRange(NotifyObjectUsers(userId, objectId).ToList());
            }

            return userIds.Distinct().ToList();
        }

        private List<int> NotifyMunicipalityUsers(int? municipalityId)
        {
            if (!municipalityId.HasValue)
            {
                return new List<int>();
            }
            using (var session = noSqlSessionFactory())
            {
                return session.Find<User>(Query.Or(Query.EQ("MunicipalityId", municipalityId.Value), Query.EQ("OriginMunicipalityId", municipalityId.Value)))
                    .SetFields("DbId").ToList().Select(u => u.DbId).ToList();
            }
        }

        private List<int> NotifyProjectMembers(string projectId)
        {
            var project = GetProject(projectId);
            var expertIds = new List<BsonObjectId>();
            foreach (var member in project.ProjectMembers)
            {
                expertIds.Add(BsonObjectId.Create(member.UserObjectId));
            }

            using (var session = noSqlSessionFactory())
            {
                return session.Find<User>(Query.In("_id", expertIds.ToArray())).SetFields("DbId").ToList().Select(u => u.DbId).ToList();
            }
        }

        private string GetIdeaVersionSubject(Idea idea, string versionId)
        {
            if (!string.IsNullOrEmpty(versionId))
            {
                var version = idea.SummaryWiki.Versions.SingleOrDefault(v => v.Id == versionId);
                if (version != null)
                {
                    return version.Subject;
                }
            }

            return null;
        }

        private WikiTextVersion GetIdeaVersion(Idea idea, string versionId)
        {
            if (!string.IsNullOrEmpty(versionId))
            {
                var version = idea.SummaryWiki.Versions.SingleOrDefault(v => v.Id == versionId);
                if (version != null)
                {
                    return version;
                }
            }

            return null;
        }

        private Data.MongoDB.Idea GetIdea(string id)
        {
            using (var session = noSqlSessionFactory())
            {
                return session.GetSingle<Data.MongoDB.Idea>(i => i.Id == id);
            }
        }

        public ToDo GetToDo(Project project, string milestoneId, string todoId)
        {
            return string.IsNullOrEmpty(milestoneId)
                           ? project.ToDos.Where(t => t.Id == todoId).SingleOrDefault()
                           : project.MileStones.Where(m => m.Id == milestoneId).Single().ToDos.Where(t => t.Id == todoId).
                                 SingleOrDefault();
        }

        private Data.MongoDB.Issue GetIssue(string id)
        {
            using (var session = noSqlSessionFactory())
            {
                return session.GetSingle<Data.MongoDB.Issue>(i => i.Id == id);
            }
        }

        private Data.MongoDB.Problem GetProblem(string id)
        {
            using (var session = noSqlSessionFactory())
            {
                return session.GetSingle<Data.MongoDB.Problem>(i => i.Id == id);
            }
        }

        private Data.MongoDB.Project GetProject(string id)
        {
            using (var session = noSqlSessionFactory())
            {
                return session.GetSingle<Data.MongoDB.Project>(i => i.Id == id);
            }
        }

        private OrganizationProject GetOrganizationProject(string organizationId, string projectId)
        {
            var org = GetOrganization(organizationId);
            return org.Projects.Single(p => p.Id == projectId);
        }

        private Organization GetOrganization(string organizationId)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                return noSqlSession.GetAll<Organization>().Single(o => o.Id == organizationId);
            }
        }

        public SubscribeModel Subscribe(string id, int userId, EntryTypes? entryType = null, bool? subscribe = null)
        {
            using (var context = actionsContextFactory.CreateContext(true))
            {
                var subscription = context.Subscribtions.SingleOrDefault(s => s.ObjectId == id && s.UserId == userId);
                if (subscription == null)
                {
                    subscription = new Subscribtion()
                    {
                        ObjectId = id,
                        UserId = userId,
                        Subscribed = true,
                        EntryTypeId = (int?)entryType
                    };
                    context.Subscribtions.Add(subscription);
                }

                if (subscribe.HasValue)
                {
                    subscription.Subscribed = subscribe.Value;
                }

                return new SubscribeModel()
                    {
                        Id = id,
                        Subscribed = subscription.Subscribed,
                        Type = (EntryTypes?)subscription.EntryTypeId
                    };
            }
        }

        public SubscribeModel IsSubscribed(string id, int userId, EntryTypes? type = null)
        {
            using (var context = actionsContextFactory.CreateContext())
            {
                var subscription = context.Subscribtions.SingleOrDefault(s => s.ObjectId == id && s.UserId == userId);
                var result = new SubscribeModel()
                {
                    Id = id,
                    Type = type
                };
                if (subscription == null)
                {
                    result.Subscribed = false;
                }
                else
                {
                    result.Subscribed = subscription.Subscribed;
                }

                return result;
            }
        }
    }
}
