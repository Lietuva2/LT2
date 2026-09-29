using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Bus.Commands;
using Data.EF.Actions;
using Data.EF.Users;
using Data.Enums;
using Data.Infrastructure.Sessions;
using Data.MongoDB;
using Data.ViewModels.Base;
using Data.ViewModels.Project;
using Framework;
using Framework.Bus;
using Framework.Infrastructure;
using Framework.Infrastructure.Storage;
using Framework.Lists;
using Framework.Mvc.Helpers;
using Framework.Mvc.Strings;
using Framework.Other;
using Framework.Strings;
using Globalization.Resources.Services;
using MongoDB.Bson;
using MongoDB.Driver.Builders;

using Services.Caching;
using Services.Classes;
using Services.Infrastructure;
using Services.Session;
using User = Data.MongoDB.User;

namespace Services.ModelServices
{
    public class ProjectService : IService
    {
        private readonly IActionsContextFactory actionSessionFactory;
        private readonly IUsersContextFactory userSessionFactory;
        private readonly Func<INoSqlSession> noSqlSessionFactory;
        private readonly IBus bus;
        private readonly ICache cache;

        public UserInfo CurrentUser { get { return MembershipSession.GetUser(); } }
        public OrganizationService OrganizationService { get { return ServiceLocator.Resolve<OrganizationService>(); } }

        public UrlHelper Url
        {
            get { return new UrlHelper(((MvcHandler)HttpContext.Current.Handler).RequestContext); }
        }

        public ProjectService(
            IActionsContextFactory actionSessionFactory,
            Func<INoSqlSession> noSqlSessionFactory,
            IUsersContextFactory userSessionFactory,
            ICache cache,
            IBus bus)
        {
            this.noSqlSessionFactory = noSqlSessionFactory;
            this.actionSessionFactory = actionSessionFactory;
            this.userSessionFactory = userSessionFactory;
            this.bus = bus;
            this.cache = cache;
        }

        public ProjectToDosModel ToDos(string projectId)
        {
            return ToDos(GetProject(projectId));
        }

        public ProjectToDosModel ToDos(Project project)
        {
            var idea = GetIdea(project.IdeaId, "StateDescription");
            var model = new ProjectToDosModel()
                            {
                                Id = project.Id,
                                IdeaId = idea.Id,
                                Subject = idea.Subject,
                                InsertResponsibleUsers = (from member in project.ProjectMembers
                                                          select new SelectListItem()
                                                                     {
                                                                         Value = member.UserObjectId,
                                                                         Text = GetUserFullName(member.UserObjectId),
                                                                     }).ToList(),
                                InsertMilestones = (from m in project.MileStones
                                                    select new SelectListItem()
                                                    {
                                                        Value = m.Id,
                                                        Text = m.DateString,
                                                    }).ToList(),
                                IsEditable = IsProjectEditable(project, idea.IsClosed),
                                IsCurrentUserInvolved = IsCurrentUserInvolved(project),
                                IsPendingConfirmation = IsPendingConfirmation(project),
                                IsJoinable = IsJoinable(project, idea),
                                StateDescription = idea.StateDescription,
                                State = IdeaStatesResource.ResourceManager.GetString(idea.ActualState.ToString())
                            };

            AddEmptyUser(model.InsertResponsibleUsers);
            model.InsertMilestones.Insert(0, new SelectListItem()
                                           {
                                               Text = CommonStrings.Unassigned,
                                               Value = string.Empty
                                           });

            model.ToDos = FilterToDos(project, project.ToDos, model.IsEditable, null);
            model.FinishedToDos = FilterFinishedToDos(project, project.ToDos, model.IsEditable, null);

            model.MileStones = (from ms in project.MileStones
                                where !ms.ToDos.Any() || ms.ToDos.Where(t => !t.FinishDate.HasValue).Any()
                                select new MileStoneModel()
                                           {
                                               ProjectId = project.Id,
                                               Date = ms.Date,
                                               Subject = ms.Subject,
                                               MileStoneId = ms.Id,
                                               IsLate = ms.Date <= DateTime.Now,
                                               ToDos = FilterToDos(project, ms.ToDos, model.IsEditable, ms.Id),
                                               FinishedToDos = FilterFinishedToDos(project, ms.ToDos, model.IsEditable, ms.Id),
                                               IsEditable = model.IsEditable
                                           }).ToList();

            model.FinishedMileStones = (from ms in project.MileStones
                                        where ms.ToDos.Any() && !ms.ToDos.Where(t => !t.FinishDate.HasValue).Any()
                                        select new MileStoneModel()
                                        {
                                            Date = ms.Date,
                                            Subject = ms.Subject,
                                            MileStoneId = ms.Id,
                                            FinishedToDos = FilterFinishedToDos(project, ms.ToDos, model.IsEditable, ms.Id),
                                            ToDos = new List<ToDoModel>(),
                                            IsEditable = model.IsEditable
                                        }).ToList();

            return model;
        }

        private List<ToDoModel> FilterToDos(Project project, List<ToDo> todos, bool isEditable, string mileStoneId)
        {
            return
                todos.Where(
                    td => !td.FinishDate.HasValue && (!td.IsPrivate || isEditable))
                    .OrderBy(td => td.Position).Select(
                        td => GetToDoModelFromToDo(td, project, isEditable, mileStoneId)).ToList();
        }

        private List<ToDoModel> FilterFinishedToDos(Project project, List<ToDo> todos, bool isEditable, string mileStoneId)
        {
            return
                todos.Where(td => td.FinishDate.HasValue && (!td.IsPrivate || isEditable))
                    .OrderByDescending(td => td.FinishDate).Select(
                        td => GetToDoModelFromToDo(td, project, isEditable, mileStoneId)).ToList();
        }

        private void AddEmptyUser(IList<SelectListItem> list)
        {
            list.Insert(0, new SelectListItem()
                               {
                                   Text = CommonStrings.All,
                                   Value = string.Empty
                               });

            list.Insert(0, new SelectListItem()
            {
                Selected = true,
                Text = CommonStrings.Unassigned,
                Value = TodoAssignedTo.Unasigned
            });
        }

        private bool IsProjectEditable(Project project, bool ideaClosed)
        {
            return IsCurrentUserInvolved(project) && !ideaClosed;
        }

        private bool IsCurrentUserInvolved(Project project)
        {
            return CurrentUser.IsAuthenticated && project.ProjectMembers.Select(m => m.UserObjectId).Contains(CurrentUser.Id);
        }

        private bool IsPendingConfirmation(Project project)
        {
            return CurrentUser.IsAuthenticated && project.UnconfirmedMembers.Select(m => m.UserObjectId).Contains(CurrentUser.Id);
        }

        public ToDoModel AddToDo(ProjectToDosModel model)
        {
            var project = GetProject(model.Id);
            var todo = new ToDo()
                           {
                               Id = BsonObjectId.GenerateNewId(),
                               Subject = model.InsertSubject,
                               ResponsibleUserId = model.InsertResponsibleUserId,
                               DueDate = model.InsertDueDate,
                               Position = project.ToDos.Where(td => !td.FinishDate.HasValue && (!td.IsPrivate || IsProjectEditable(project, false))).Count(),
                               IsPrivate = model.InsertToDoIsPrivate,
                               CreatedByUserId = CurrentUser.Id
                           };
            if (!string.IsNullOrEmpty(model.InsertMileStoneId))
            {
                var milestone = project.MileStones.Where(m => m.Id == model.InsertMileStoneId).Single();
                todo.Position =
                    milestone.ToDos.Where(
                        td => !td.FinishDate.HasValue && (!td.IsPrivate || IsProjectEditable(project, false))).Count();
                milestone.ToDos.Add(todo);
            }
            else
            {
                project.ToDos.Add(todo);
            }

            UpdateProject(project);

            var idea = GetIdea(project.IdeaId);
            bus.Send(new ProjectCommand()
                         {
                             ActionType = ActionTypes.ToDoAdded,
                             ProjectId = project.Id,
                             UserId = CurrentUser.Id,
                             ToDoId = todo.Id,
                             Text = todo.Subject,
                             ProjectSubject = idea.Subject,
                             Link = GetProjectUrl(project.Id),
                             MileStoneId = model.InsertMileStoneId,
                             IsPrivate = idea.IsPrivateToOrganization && !string.IsNullOrEmpty(idea.OrganizationId) || model.InsertToDoIsPrivate,
                             SendNotifications = model.InsertSendNotifications,
                             UserFullName = CurrentUser.FullName,
                             UserLink = Url.ActionAbsolute("Details", "Account", new {userObjectId = CurrentUser.Id})
                         });
            return GetToDoModelFromToDo(todo, project, IsProjectEditable(project, false), model.InsertMileStoneId);
        }

        private ToDoModel GetToDoModelFromToDo(ToDo todo, Project project, bool isEditable, string mileStoneId)
        {
            return new ToDoModel()
            {
                ToDoId = todo.Id,
                ProjectId = project.Id,
                DueDate = todo.DueDate,
                FinishDate = todo.FinishDate,
                IsFinished = todo.FinishDate.HasValue,
                Position = todo.Position,
                CommentsCount = todo.Comments.Where(c => !c.IsPrivate || isEditable).Count(),
                ResponsibleUserId = todo.ResponsibleUserId,
                ResponsibleUserFullName = GetUserFullName(todo.ResponsibleUserId),
                Subject = todo.Subject,
                IsEditable = isEditable,
                IsPrivate = todo.IsPrivate,
                IsLate = todo.DueDate <= DateTime.Now,
                MileStoneId = mileStoneId,
                CreatedByUserId = todo.CreatedByUserId,
                CreatedByUserFullName = todo.CreatedByUserId != null ? GetUserFullName(todo.CreatedByUserId) : null
            };
        }

        public ToDoModel FinishToDo(string projectId, string milestoneId, string id)
        {
            var project = GetProject(projectId);
            var todo = GetToDo(project, milestoneId, id);
            MileStone milestone = !string.IsNullOrEmpty(milestoneId)
                                      ? project.MileStones.Where(m => m.Id == milestoneId).Single()
                                      : null;
            var idea = GetIdea(project.IdeaId);
            if (todo.FinishDate.HasValue)
            {
                todo.FinishDate = null;
                if (milestone == null)
                {
                    todo.Position = GetSortedTodos(project).Count - 1;
                }
                else
                {
                    todo.Position = GetSortedTodos(milestone).Count - 1;
                }
                bus.Send(new ToDoUnfinishedCommand()
                             {
                                 ToDoId = todo.Id
                             });
            }
            else
            {
                todo.FinishDate = DateTime.Now;
                todo.Position = null;
                if (milestone == null)
                {
                    UpdateToDoOrder(project);
                }
                else
                {
                    UpdateToDoOrder(milestone);
                }

                bus.Send(new ProjectCommand()
                             {
                                 ActionType = ActionTypes.ToDoFinished,
                                 ProjectId = project.Id,
                                 UserId = CurrentUser.Id,
                                 ToDoId = todo.Id,
                                 Text = todo.Subject,
                                 ProjectSubject = idea.Subject,
                                 Link = GetProjectUrl(project.Id),
                                 MileStoneId = milestoneId,
                                 IsPrivate = idea.IsPrivateToOrganization && !string.IsNullOrEmpty(idea.OrganizationId) || todo.IsPrivate
                             });
            }

            UpdateProject(project);

            return GetToDoModelFromToDo(todo, project, IsProjectEditable(project, false), milestoneId);
        }

        public bool DeleteToDo(string projectId, string milestoneId, string id)
        {
            var project = GetProject(projectId);
            var todo = GetToDo(project, milestoneId, id);
            if (string.IsNullOrEmpty(milestoneId))
            {
                project.ToDos.Remove(todo);
            }
            else
            {
                project.MileStones.Where(m => m.Id == milestoneId).Single().ToDos.Remove(todo);
            }

            UpdateToDoOrder(project);
            UpdateProject(project);

            bus.Send(new ToDoDeletedCommand()
            {
                ToDoId = todo.Id
            });

            return true;
        }

        public EditToDoModel GetToDoEdit(string projectId, string milestoneId, string id)
        {
            var project = GetProject(projectId);
            var todo = GetToDo(project, milestoneId, id);
            var model = new EditToDoModel()
            {
                Id = todo.Id,
                ProjectId = project.Id,
                DueDate = todo.DueDate,
                ResponsibleUserId = todo.ResponsibleUserId ?? TodoAssignedTo.All,
                Subject = todo.Subject,
                ResponsibleUsers = (from member in project.ProjectMembers
                                    select new SelectListItem()
                                    {
                                        Value = member.UserObjectId,
                                        Text = GetUserFullName(member.UserObjectId),
                                    }).ToList(),
                IsPrivate = todo.IsPrivate,
                MileStoneId = milestoneId,
                Milestones = (from m in project.MileStones
                              select new SelectListItem()
                              {
                                  Value = m.Id,
                                  Text = m.DateString,
                              }).ToList()
            };

            model.Milestones.Insert(0, new SelectListItem()
            {
                Text = CommonStrings.Unassigned,
                Value = string.Empty
            });

            AddEmptyUser(model.ResponsibleUsers);
            return model;
        }

        public ToDoModel EditToDo(EditToDoModel model)
        {
            var project = GetProject(model.ProjectId);
            var idea = GetIdea(project.IdeaId);
            var todo = GetToDo(project, model.MileStoneId, model.Id);
            todo.Subject = model.Subject;
            todo.DueDate = model.DueDate;
            todo.ResponsibleUserId = model.IsPrivate ? CurrentUser.Id : model.ResponsibleUserId;
            todo.IsPrivate = idea.IsPrivateToOrganization && !string.IsNullOrEmpty(idea.OrganizationId) || model.IsPrivate;
            UpdateProject(project);

            bus.Send(new ProjectCommand()
                         {
                             ActionType = ActionTypes.ToDoEdited,
                             ProjectId = project.Id,
                             UserId = CurrentUser.Id,
                             ToDoId = todo.Id,
                             Text = todo.Subject,
                             ProjectSubject = idea.Subject,
                             Link = GetProjectUrl(project.Id),
                             MileStoneId = model.MileStoneId,
                             IsPrivate = todo.IsPrivate
                         });

            return GetToDoModelFromToDo(todo, project, IsProjectEditable(project, false), model.MileStoneId);
        }

        public void ReorderToDos(string projectId, string milestoneId, int startPos, int endPos)
        {
            var project = GetProject(projectId);
            ToDo todo;
            List<ToDo> sortedList;
            int finalPos = -1;
            if (string.IsNullOrEmpty(milestoneId))
            {
                var list = FilterToDos(project, project.ToDos, true, null);
                var td = list[startPos];
                todo = project.ToDos.Where(t => t.Id == td.ToDoId).Single();
                sortedList = GetSortedTodos(project);
                finalPos = list[endPos].Position.Value;

            }
            else
            {
                var milestone = project.MileStones.Where(m => m.Id == milestoneId).Single();
                var list = FilterToDos(project, milestone.ToDos, true, milestone.Id);
                var td = list[startPos];
                todo = milestone.ToDos.Where(t => t.Id == td.ToDoId).Single();
                sortedList = GetSortedTodos(milestone);
                finalPos = list[endPos].Position.Value;
            }
            if (finalPos < 0) finalPos = 0;
            sortedList.Remove(todo);
            sortedList.Insert(finalPos, todo);
            UpdateToDoOrder(sortedList);
            UpdateProject(project);
        }

        private void UpdateToDoOrder(List<ToDo> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                list[i].Position = i;
            }
        }

        private void UpdateToDoOrder(Project project)
        {
            UpdateToDoOrder(GetSortedTodos(project));
        }

        private void UpdateToDoOrder(MileStone mileStone)
        {
            UpdateToDoOrder(GetSortedTodos(mileStone));
        }

        private List<ToDo> GetSortedTodos(Project project)
        {
            return project.ToDos.Where(t => !t.FinishDate.HasValue).OrderBy(t => t.Position).ToList();
        }

        private List<ToDo> GetSortedTodos(MileStone mileStone)
        {
            return mileStone.ToDos.Where(t => !t.FinishDate.HasValue).OrderBy(t => t.Position).ToList();
        }

        private Project GetProject(MongoObjectId projectId)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                return noSqlSession.GetById<Project>(projectId);
            }
        }

        private Idea GetIdea(MongoObjectId ideaId)
        {
            return GetIdea(ideaId, new string[]{});
        }

        private Idea GetIdea(MongoObjectId ideaId, params string[] additionalFields)
        {
            string[] defaultFields = new string[] { "_id", "Subject", "State", "Deadline", "VotesCount", "RequiredVotes" };
            additionalFields = defaultFields.Union(additionalFields).ToArray();
            using (var noSqlSession = noSqlSessionFactory())
            {
                return noSqlSession.Find<Idea>(Query.EQ("_id", ideaId)).SetFields(additionalFields).SingleOrDefault();
            }
        }

        private void UpdateProject(Project project)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                noSqlSession.Update(project);
            }
        }

        private string GetUserFullName(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return CommonStrings.All;
            }

            if (id == TodoAssignedTo.Unasigned)
            {
                return CommonStrings.Unassigned;
            }

            using (var session = noSqlSessionFactory())
            {
                MongoObjectId mid = id;
                var user = session.Find<Data.MongoDB.User>(Query.EQ("_id", mid)).SetFields("FirstName", "LastName").SingleOrDefault();
                if (user != null)
                {
                    return user.FullName;
                }

                return string.Empty;
            }
        }

        public CommentsModel GetComments(string projectId, string milestoneId, string toDoId)
        {
            var project = GetProject(projectId);
            var idea = GetIdea(project.IdeaId);
            var todo = GetToDo(project, milestoneId, toDoId);
            var isInvolved = IsCurrentUserInvolved(project);
            var model = new CommentsModel()
                            {
                                IdeaSubject = idea.Subject,
                                IsEditable = IsProjectEditable(project, idea.IsClosed),
                                IsTodoPrivate = todo.IsPrivate,
                                InsertIsPrivate = todo.IsPrivate,
                                Comments = (from c in todo.Comments
                                            orderby c.Date
                                            where c.IsPrivate == false || isInvolved
                                            select GetCommentModelFromComment(c, project, milestoneId, toDoId, idea.IsClosed)).ToList()
                            };
            model.ToDo = GetToDoModelFromToDo(todo, project, model.IsEditable, milestoneId);
            return model;
        }

        public CommentModel AddComment(string projectId, string milestoneId, string toDoId, string text, bool isPrivate, bool sendNotifications, List<UrlViewModel> attachments)
        {
            var project = GetProject(projectId);
            var todo = GetToDo(project, milestoneId, toDoId);
            var comment = new ToDoComment()
                              {
                                  Date = DateTime.Now,
                                  Text = text,
                                  UserFullName = CurrentUser.FullName,
                                  UserObjectId = CurrentUser.Id,
                                  IsPrivate = isPrivate
                              };

            attachments.BindUrls(comment.Attachments);

            todo.Comments.Add(comment);
            UpdateProject(project);

            var idea = GetIdea(project.IdeaId);

            bus.Send(new ProjectCommand()
                         {
                             ActionType = ActionTypes.ToDoCommentAdded,
                             ProjectId = project.Id,
                             MileStoneId = milestoneId,
                             UserId = CurrentUser.Id,
                             ToDoId = todo.Id,
                             Text = comment.Text,
                             ProjectSubject = idea.Subject,
                             Link = GetProjectUrl(project.Id),
                             ToDoSubject = todo.Subject,
                             ToDoLink = Url.ActionAbsolute("Comments", "Project", new {projectId = project.Id, toDoId = todo.Id}),
                             IsPrivate = idea.IsPrivateToOrganization && !string.IsNullOrEmpty(idea.OrganizationId) || todo.IsPrivate || comment.IsPrivate,
                             CommentId = comment.Id,
                             SendNotifications = sendNotifications,
                             UserFullName = CurrentUser.FullName,
                             UserLink = Url.ActionAbsolute("Details", "Account", new { userObjectId = CurrentUser.Id })
                         });

            return GetCommentModelFromComment(comment, project, milestoneId, toDoId, false);
        }

        public CommentModel GetCommentModelFromComment(ToDoComment c, Project project, string milestoneId, string toDoId, bool isIdeaClosed)
        {
            return new CommentModel()
                       {
                           AuthorFullName = c.UserFullName,
                           AuthorObjectId = c.UserObjectId,
                           CommentDate = c.Date,
                           CommentText = c.Text.NewLineToHtml().ActivateLinks(),
                           ProjectId = project.Id,
                           Id = c.Id,
                           MileStoneId = milestoneId,
                           ToDoId = toDoId,
                           IsDeletable =
                               CurrentUser.IsAuthenticated && c.UserObjectId == CurrentUser.Id &&
                               IsProjectEditable(project, isIdeaClosed),
                            Attachments = c.Attachments.Select(a => new UrlViewModel()
                                                                        {
                                                                            IconUrl = a.IconUrl,
                                                                            Url = a.Url,
                                                                            Title = a.Title
                                                                        }).ToList()
                       };
        }

        public bool DeleteComment(string projectId, string milestoneId, string toDoId, string commentId)
        {
            var project = GetProject(projectId);
            var todo = GetToDo(project, milestoneId, toDoId);
            var comment = todo.Comments.Where(c => c.Id == commentId).Single();
            todo.Comments.Remove(comment);
            UpdateProject(project);
            bus.Send(new CommentDeletedCommand()
            {
                CommentId = comment.Id,
                UserObjectId = CurrentUser.Id
            });
            return true;
        }

        private string GetProjectUrl(string projectId)
        {
            return Url.ActionAbsolute("ToDos", "Project", new {projectId = projectId});
        }

        public ProjectTeamModel GetProjectTeam(string projectId)
        {
            Project project = GetProject(projectId);
            var idea = GetIdea(project.IdeaId, "OrganizationId");
            var model = new ProjectTeamModel()
                       {
                           Id = projectId,
                           IdeaId = idea.Id,
                           Subject = idea.Subject,
                           IsCurrentUserInvolved = IsCurrentUserInvolved(project),
                           IsPendingConfirmation = IsPendingConfirmation(project),
                           IsJoinable = IsJoinable(project, idea),
                           OrganizationId = idea.OrganizationId,
                           Members = (from member in project.ProjectMembers
                                      select new MemberModel()
                                                 {
                                                     ObjectId = member.UserObjectId,
                                                     FullName = GetUserFullName(member.UserObjectId),
                                                     Role = member.Role
                                                 }).ToList(),
                           UnconfirmedMembers = (from member in project.UnconfirmedMembers
                                      select new UserLinkModel()
                                      {
                                          ObjectId = member.UserObjectId,
                                          FullName = GetUserFullName(member.UserObjectId)
                                      }).ToList()
                       };
            using (var userSession = userSessionFactory.CreateContext())
            {
                model.InvitedUsers =
                    userSession.UserInvitations.Where(
                        ui => ui.ProjectId == projectId && !ui.Joined)
                        .Select(ui => new InviteUserModel()
                        {
                            InvitedUser = ui.UserEmail,
                            InvitationSent = true,
                            Message = ui.Message
                        }).ToList();
            }
            return model;
        }

        public MileStoneEditModel MileStones(string projectId)
        {
            var project = GetProject(projectId);
            var idea = GetIdea(project.IdeaId);
            var model = new MileStoneEditModel()
                            {
                                Id = project.Id,
                                IdeaId = project.IdeaId,
                                Subject = idea.Subject,
                                IsEditable = IsProjectEditable(project, idea.IsClosed),
                                IsCurrentUserInvolved = IsCurrentUserInvolved(project),
                                IsPendingConfirmation = IsPendingConfirmation(project),
                                IsJoinable = IsJoinable(project, idea),
                                MileStones = (from m in project.MileStones
                                              select GetMileStoneModelFromMileStone(project, m, IsProjectEditable(project, idea.IsClosed))).ToList()
                            };
            
            return model;
        }

        public MileStoneModel AddMileStone(MileStoneEditModel model)
        {
            var project = GetProject(model.Id);
            var milestone = new MileStone()
            {
                Subject = model.InsertSubject,
                Date = model.InsertDate
            };
            project.MileStones.Add(milestone);
            UpdateProject(project);

            var idea = GetIdea(project.IdeaId);

            //bus.Send(new ProjectCommand()
            //{
            //    ActionType = ActionTypes.MileStoneAdded,
            //    ProjectId = project.Id,
            //    UserDbId = CurrentUser.DbId,
            //    MileStoneId = todo.Id,
            //    Text = todo.Subject,
            //    Subject = idea.Subject,
            //    Link = GetProjectUrl(project.Id)
            //});

            return GetMileStoneModelFromMileStone(project, milestone, IsProjectEditable(project, idea.IsClosed));
        }

        public bool DeleteMileStone(string projectId, string id)
        {
            var project = GetProject(projectId);
            var todo = project.MileStones.Where(t => t.Id == id).SingleOrDefault();
            project.MileStones.Remove(todo);
            UpdateProject(project);
            return true;
        }

        public EditMileStoneModel GetMileStoneEdit(string projectId, string id)
        {
            var project = GetProject(projectId);
            var todo = project.MileStones.Where(t => t.Id == id).SingleOrDefault();
            var model = new EditMileStoneModel()
            {
                Id = todo.Id,
                ProjectId = project.Id,
                Date = todo.Date,
                Subject = todo.Subject,
            };

            return model;
        }

        public MileStoneModel EditMileStone(EditMileStoneModel model)
        {
            var project = GetProject(model.ProjectId);
            var mileStone = project.MileStones.Where(t => t.Id == model.Id).SingleOrDefault();
            mileStone.Subject = model.Subject;
            mileStone.Date = model.Date;
            UpdateProject(project);

            //var idea = GetIdea(project.IdeaId);

            //bus.Send(new ProjectCommand()
            //{
            //    ActionType = ActionTypes.MileStoneEdited,
            //    ProjectId = project.Id,
            //    UserDbId = CurrentUser.DbId,
            //    MileStoneId = todo.Id,
            //    Text = todo.Subject,
            //    Subject = idea.Subject,
            //    Link = GetProjectUrl(project.Id)
            //});

            return GetMileStoneModelFromMileStone(project, mileStone, IsProjectEditable(project, false));
        }

        private MileStoneModel GetMileStoneModelFromMileStone(Project project, MileStone mileStone, bool isEditable)
        {
            var model = new MileStoneModel()
                       {
                           Date = mileStone.Date,
                           Subject = mileStone.Subject,
                           MileStoneId = mileStone.Id,
                           IsEditable = isEditable,
                           ProjectId = project.Id,
                           IsFinished = mileStone.ToDos.Count > 0 && !mileStone.ToDos.Where(t => !t.FinishDate.HasValue).Any()
                       };

            model.IsLate = !model.IsFinished && mileStone.Date.HasValue && mileStone.Date.Value < DateTime.Now;
            return model;
        }

        public ToDo GetToDo(Project project, string milestoneId, string todoId)
        {
            var todo = string.IsNullOrEmpty(milestoneId)
                           ? project.ToDos.Where(t => t.Id == todoId).SingleOrDefault()
                           : project.MileStones.Where(m => m.Id == milestoneId).Single().ToDos.Where(t => t.Id == todoId).
                                 SingleOrDefault();
            if(todo == null)
            {
                foreach(var mileStone in project.MileStones)
                {
                    todo = mileStone.ToDos.Where(t => t.Id == todoId).SingleOrDefault();
                    if(todo != null)
                    {
                        mileStone.ToDos.Remove(todo);
                        if (!string.IsNullOrEmpty(milestoneId))
                        {
                            var ms = project.MileStones.Where(m => m.Id == milestoneId).Single();
                            ms.ToDos.Add(todo);
                            UpdateToDoOrder(ms.ToDos);
                        }
                        else
                        {
                            project.ToDos.Add(todo);
                            UpdateToDoOrder(project.ToDos);
                        }

                        UpdateToDoOrder(mileStone.ToDos);
                        
                        return todo;
                    }
                }

                todo = project.ToDos.Where(t => t.Id == todoId).SingleOrDefault();
                if(todo != null)
                {
                    project.ToDos.Remove(todo);
                    var ms = project.MileStones.Where(m => m.Id == milestoneId).Single();
                    ms.ToDos.Add(todo);
                    UpdateToDoOrder(ms.ToDos);
                    UpdateToDoOrder(project.ToDos);
                }
            }

            return todo;
        }

        public SettingsModel GetSettings(string projectId)
        {
            var project = GetProject(projectId);
            var idea = GetIdea(project.IdeaId, "StateDescription");
            return new SettingsModel()
                       {
                           Id = project.Id,
                           Description = idea.StateDescription,
                           IdeaId = project.IdeaId,
                           IsPrivate = project.IsPrivate,
                           State = idea.ActualState.ToString(),
                           Subject = idea.Subject,
                           ProjectStates = GetStates(idea.ActualState)
                       };
        }

        public void SaveSettings(SettingsModel model)
        {
            var project = GetProject(model.Id);
            
            project.IsPrivate = model.IsPrivate;
            using (var noSqlSession = noSqlSessionFactory())
            {
                var idea = noSqlSession.GetById<Idea>(project.IdeaId);
                var initialState = idea.ActualState;
                if (model.State != null)
                {
                    idea.State = (IdeaStates) Enum.Parse(typeof (IdeaStates), model.State);
                }
                idea.StateDescription = model.Description.RemoveNewLines().Sanitize();
                noSqlSession.Update(project);
                noSqlSession.Update(idea);

                if (initialState != idea.ActualState)
                {
                    bus.Send(new IdeaCommand()
                                 {
                                     ActionType = ActionTypes.IdeaStateChanged,
                                     ObjectId = idea.Id,
                                     UserId = CurrentUser.Id,
                                     Text = IdeaStatesResource.ResourceManager.GetString(idea.ActualState.ToString()) + (!string.IsNullOrEmpty(idea.StateDescription) ? ": " + idea.StateDescription : string.Empty),
                                     IsPrivate = idea.IsPrivateToOrganization && !string.IsNullOrEmpty(idea.OrganizationId),
                                     Link = Url.Action("Details", "Idea", new { id = idea.Id })
                                 });
                }
            }
        }

        private IList<SelectListItem> GetStates(IdeaStates currentState)
        {
            var initialState = "New";
            if (currentState.In(IdeaStates.Resolved, IdeaStates.Rejected))
            {
                initialState = currentState.ToString();
            }

            var states = new string[] { initialState, "Implementation", "Realized", "Closed" };
            return states.Select(s => new SelectListItem()
            {
                Text = IdeaStatesResource.ResourceManager.GetString(s),
                Value = s
            }).ToList();
        }

        public void ConfirmMember(string projectId, string userId)
        {
            var project = GetProject(projectId);
            var member = project.UnconfirmedMembers.Where(m => m.UserObjectId == userId).Single();
            project.UnconfirmedMembers.Remove(member);
            project.ProjectMembers.Add(member);
            UpdateProject(project);
        }

        public ProjectTeamModel GetProjectMembersEdit(string projectId)
        {
            return GetProjectTeam(projectId);
        }

        public ProjectTeamModel SaveProjectMembers(ProjectTeamModel model)
        {
            var project = GetProject(model.Id);
            foreach(var member in project.ProjectMembers)
            {
                var mem = member;
                member.Role = model.Members.Where(m => m.ObjectId == mem.UserObjectId).Single().Role;
            }

            UpdateProject(project);
            return GetProjectTeam(model.Id);
        }

        public bool IsJoinable(Project project, Idea idea)
        {
            return !idea.IsClosed && !IsCurrentUserInvolved(project) && CurrentUser.IsAuthenticated && !project.UnconfirmedMembers.Where(m => m.UserObjectId == CurrentUser.Id).Any();
        }

        public SimpleListContainerModel GetMyProjects(int pageNumber, string userObjectId)
        {
            using (var actionSession = actionSessionFactory.CreateContext())
            {
                var result = (from a in actionSession.Actions
                            where a.UserObjectId == userObjectId && a.ActionTypeId == (int)ActionTypes.JoinedProject && 
                            (!a.IsPrivate || CurrentUser.OrganizationIds.Contains(a.OrganizationId)) && !a.IsDeleted
                            group a by new { Id = a.ObjectId, Subject = a.Subject } into g
                select new SimpleListModel
                {
                    Id = g.Key.Id,
                    Subject = g.Key.Subject,
                    Date = g.Max(a => a.Date)
                }).OrderByDescending(i => i.Date).GetExpandablePage(pageNumber, CustomAppSettings.PageSizeList).ToList();
                result.ForEach(r => r.Type = EntryTypes.Project);
                var simpleList = new SimpleListContainerModel();
                simpleList.List = new ExpandableList<SimpleListModel>(result, CustomAppSettings.PageSizeList);
                return simpleList;
            }
        }
        public bool InviteUsers(ProjectTeamModel model)
        {
            var project = GetProject(model.Id);

            foreach (var userId in model.UsersToInvite.Where(u => u.UserId.HasValue).Select(u => u.UserId).Distinct())
            {
                AddMember(project, userId.Value);
            }


            foreach (var user in model.UsersToInvite.Where(u => !u.UserId.HasValue).Select(u => u.InvitedUser).Distinct())
            {
                InviteUser(project, user);
            }

            return true;
        }

        public bool InviteUser(string projectId, string email)
        {
            return InviteUser(GetProject(projectId), email);
        }

        public bool InviteUser(Project project, string email)
        {
            using (var session = userSessionFactory.CreateContext(true))
            {
                var existing = session.UserEmails.SingleOrDefault(us => us.Email == email);

                if (existing == null)
                {
                    string projectId = project.Id.ToString();
                    var u = session.UserInvitations.SingleOrDefault(uo => uo.UserEmail == email && uo.ProjectId == projectId);

                    if (u == null)
                    {
                        u = new Data.EF.Users.UserInvitation()
                        {
                            ProjectId = projectId,
                            UserEmail = email
                        };
                        session.UserInvitations.Add(u);
                    }

                    bus.Send(new ProjectInviteCommand()
                    {
                        ProjectLink =
                            Url.ActionAbsolute("ToDos", "Project", new { projectId }),
                        ProjectId = projectId,
                        UserFullName = CurrentUser.FullName,
                        UserLink =
                            Url.ActionAbsolute("Details", "Account", new { userObjectId = CurrentUser.Id }),
                        Email = email
                    });
                }
                else
                {
                    AddMember(project, existing.User.Id);
                }
            }

            return true;
        }

        public bool AddMember(Project project, int userId)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                var userObjectId =
                    noSqlSession.GetAll<User>().Where(u => u.DbId == userId).Select(u => u.Id).Single();
                if(project.ProjectMembers.Select(p => p.UserObjectId).Contains(userObjectId.ToString()))
                {
                    return false;
                }

                project.ProjectMembers.Add(new ProjectMember() {UserObjectId = userObjectId});

                noSqlSession.Update(project);

                bus.Send(new ProjectMemberAddCommand()
                {
                    ObjectId = project.Id,
                    UserId = CurrentUser.Id,
                    AddedUserId = userId,
                    Link = GetProjectUrl(project.Id)
                });

                return true;
            }
        }

        public bool RemoveMember(string projectId, string memberId)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                var project = GetProject(projectId);
                var member = project.ProjectMembers.SingleOrDefault(m => m.UserObjectId == memberId);
                if (member == null)
                {
                    return false;
                }

                project.ProjectMembers.Remove(member);

                noSqlSession.Update(project);

                return true;
            }
        }

        public List<MemberModel> GetMemberConfirmedEmails(Project project)
        {
            using (var session = userSessionFactory.CreateContext())
            {
                return (from m in project.ProjectMembers
                        from u in session.UserEmails
                        where u.IsEmailConfirmed && u.SendMail && u.User.ObjectId == m.UserObjectId
                        select new MemberModel()
                            {
                                FullName = u.User.FirstName + " " + u.User.LastName,
                                ObjectId = u.User.ObjectId,
                                Email = u.Email
                            }).ToList();
            }
        }

        public Data.EF.Users.User GetDbUser(string userObjectId)
        {
            using (var session = userSessionFactory.CreateContext())
            {
                return
                    session.Users.SingleOrDefault(u => u.ObjectId == userObjectId) ?? new Data.EF.Users.User();
            }
        }

        public List<MemberModel> GetCommentUserEmails(ToDo todo)
        {
            using (var session = userSessionFactory.CreateContext())
            {
                return (from c in todo.Comments
                        from u in GetDbUser(c.UserObjectId).UserEmails
                        where u.IsEmailConfirmed && u.SendMail
                        select new MemberModel()
                            {
                                FullName = u.User.FirstName + " " + u.User.LastName,
                                ObjectId = u.User.ObjectId,
                                Email = u.Email
                            }).ToList();
            }
        }

        public bool AddOrganizationMembers(string projectId)
        {
            var project = GetProject(projectId);
            var idea = GetIdea(project.IdeaId, "OrganizationId");
            var members = OrganizationService.GetConfirmedMembers(idea.OrganizationId);
            foreach (var member in members)
            {
                if (member.DbId.HasValue)
                {
                    AddMember(project, member.DbId.Value);
                }
            }

            return true;
        }

        public ToDoModel TakeToDo(string projectId, string mileStoneId, string toDoId)
        {
            var project = GetProject(projectId);
            var todo = GetToDo(project, mileStoneId, toDoId);
            var idea = GetIdea(project.IdeaId);
            todo.ResponsibleUserId = CurrentUser.Id;
            UpdateProject(project);

            bus.Send(new ProjectCommand()
            {
                ActionType = ActionTypes.ToDoTaken,
                ProjectId = project.Id,
                UserId = CurrentUser.Id,
                ToDoId = todo.Id,
                Text = todo.Subject,
                ProjectSubject = idea.Subject,
                Link = GetProjectUrl(project.Id),
                MileStoneId = mileStoneId,
                IsPrivate = todo.IsPrivate
            });

            return GetToDoModelFromToDo(todo, project, IsProjectEditable(project, false), mileStoneId);
        }
    }
}