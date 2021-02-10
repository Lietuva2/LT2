using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using Bus.Commands;
using Data.EF.Actions;
using Data.EF.Users;
using Data.EF.Voting;
using Data.Enums;
using Data.Infrastructure.Sessions;
using Data.MongoDB;
using Framework;
using Framework.Hashing;
using Framework.Infrastructure.Logging;
using Framework.Strings;
using Services.Notifications;
using User = Data.EF.Users.User;

namespace Services.ModelServices
{
    public class NotificationService
    {
        private readonly IActionsContextFactory actionsSessionFactory;
        private readonly IUsersContextFactory usersSessionFactory;
        private readonly IVotingContextFactory votingSessionFactory;
        private readonly Func<INoSqlSession> noSqlSessionFactory;
        private readonly ProjectService projectService;
        private readonly OrganizationService organizationService;
        private readonly ILogger logger;

        public NotificationService(
            IActionsContextFactory actionsSessionFactory,
            IUsersContextFactory usersSessionFactory,
            ProjectService projectService,
            OrganizationService organizationService,
            Func<INoSqlSession> noSqlSessionFactory,
            IVotingContextFactory votingSessionFactory,
            ILogger logger)
        {
            this.actionsSessionFactory = actionsSessionFactory;
            this.usersSessionFactory = usersSessionFactory;
            this.projectService = projectService;
            this.noSqlSessionFactory = noSqlSessionFactory;
            this.votingSessionFactory = votingSessionFactory;
            this.organizationService = organizationService;
            this.logger = logger;
        }

        public Data.EF.Users.Notification GetNotification(int id)
        {
            using (var session = usersSessionFactory.CreateContext())
            {
                return session.Notifications.Where(u => u.Id == id).SingleOrDefault();
            }
        }

        public void SendProjectInviteNotification(InviteNotification notification, ProjectInviteCommand command)
        {
            var objectName = string.Empty;
            using (var session = noSqlSessionFactory())
            {
                objectName =
                    session.GetAll<Data.MongoDB.Idea>().Where(o => o.ProjectId == command.ProjectId).Select(
                        o => o.Subject).SingleOrDefault();
            }

            SendInviteNotification(notification, (int)NotificationTypes.ProjectInvite, command.UserFullName, command.UserLink, objectName, command.ProjectLink, command.Email, string.Empty, string.Empty, u => u.ProjectId == command.ProjectId);
        }

        public void SendOrganizationInviteNotification(InviteNotification notification,
                                                       OrganizationInviteCommand command)
        {
            var objectName = string.Empty;
            var customText = string.Empty;
            var userRole = string.Empty;
            using (var session = noSqlSessionFactory())
            {
                var org =
                    session.GetAll<Data.MongoDB.Organization>().Where(o => o.Id == command.OrganizationId).Select(
                        o => new {o.Name, o.CustomInvitationText}).SingleOrDefault();
                objectName = org.Name;
                customText = org.CustomInvitationText;
            }

            using (var session = actionsSessionFactory.CreateContext())
            {
                var member =
                    session.UserInterestingOrganizations.SingleOrDefault(
                        u => u.OrganizationId == command.OrganizationId && u.UserId == command.UserId);

                userRole = member.Role;
            }

            SendInviteNotification(notification, (int) NotificationTypes.OrganizationInvite, command.UserFullName,
                                   command.UserLink, objectName, command.OrganizationLink, command.Email, userRole, customText,
                                   u => u.OrganizationId == command.OrganizationId);
        }

        private void SendInviteNotification(InviteNotification notification, int notificationId, string userFullName, string userLink, string objectName, string objectLink, string email, string role, string customText, Func<UserInvitation, bool> predicate)
        {
            notification.UserFullName = userFullName;
            notification.UserLink = userLink;
            notification.ObjectName = objectName;
            notification.ObjectLink = objectLink;
            var not = GetNotification(notificationId);
            notification.MessageTemplate = not.Message;
            notification.Subject = not.Subject;
            notification.Exception = null;
            notification.UserRole = role;
            notification.CustomText = customText;

            notification.To = email;

            using (var session = usersSessionFactory.CreateContext(true))
            {
                var inv = session.UserInvitations.Where(u => u.UserEmail == email).Where(predicate).SingleOrDefault();

                try
                {
                    var success = notification.Execute();

                    if (inv != null)
                    {
                        if (success)
                        {
                            inv.InvitationSent++;
                        }
                        else if (notification.Exception != null)
                        {
                            inv.Message = notification.Exception.Message;
                        }
                    }
                }
                catch (Exception e)
                {
                    if (inv != null)
                    {
                        inv.Message = e.Message;
                    }
                }
            }
        }

        public void SendChangePasswordNotification(ChangePasswordNotification notification, ChangePasswordCommand command)
        {
            notification.To = command.Email;
            notification.Password = command.Password;
            notification.Username = command.UserName;

            using (var session = usersSessionFactory.CreateContext())
            {
                var not = session.Notifications.Single(n => n.Id == (int)NotificationTypes.ChangePassword);
                notification.MessageTemplate = not.Message;
                notification.Subject = not.Subject;
                notification.Execute();
            }
        }

        public void SendUserCreatedNotification(ConfirmNotification notification, UserCreatedCommand command)
        {
            using (var session = usersSessionFactory.CreateContext())
            {
                SendConfirmEmailNotification(notification,
                                             session.Users.Where(u => u.ObjectId == command.UserId)
                                                    .Select(u => u.UserEmails.Select(e => e.Email).FirstOrDefault()).SingleOrDefault());
            }
        }

        public void SendEmailChangedNotification(ConfirmNotification notification, EmailChangedCommand command)
        {
            SendConfirmEmailNotification(notification, command.Email);
        }

        private void SendConfirmEmailNotification(ConfirmNotification notification, string email)
        {
            using (var session = usersSessionFactory.CreateContext(true))
            {
                var user = session.UserEmails.Single(u => u.Email == email);
                if (!user.IsEmailConfirmed)
                {
                    notification.To = user.Email;
                    notification.Email = user.Email;
                    notification.ConfirmationCode = new PasswordGenerator().GeneratePassword(10, true, true, false);
                    user.EmailConfirmationCode = notification.ConfirmationCode;
                    var not = session.Notifications.Single(n => n.Id == (int)NotificationTypes.ConfirmEmail);
                    notification.MessageTemplate = not.Message;
                    notification.Subject = not.Subject;
                    notification.Execute();
                }
            }
        }

        public void SendIdeaResolvedNotification(IdeaResolvedNotification notification, string ideaId, string link)
        {
            var users = new List<string>();
            using (var session = usersSessionFactory.CreateContext())
            {
                using (var noSqlsession = noSqlSessionFactory())
                {
                    var idea = noSqlsession.GetSingle<Data.MongoDB.Idea>(i => i.Id == ideaId);
                    if (idea == null)
                    {
                        return;
                    }

                    notification.ObjectSubject = idea.Subject;
                    notification.Deadline = idea.Deadline.HasValue ? idea.Deadline.Value.ToString() : "Nėra";
                    if (idea.InitiativeType.HasValue)
                    {
                        notification.InitiativeType = Globalization.Resources.Services.InitiativeTypes.ResourceManager.GetString(idea.InitiativeType.ToString());
                    }
                    else
                    {
                        notification.InitiativeType = "Pilietinė iniciatyva";
                    }

                    foreach (var version in idea.SummaryWiki.Versions)
                    {
                        foreach (var su in version.SupportingUsers)
                        {
                            foreach (var email in session.UserEmails.Where(u => u.User.ObjectId == su.Id && u.IsEmailConfirmed && u.SendMail))
                            {
                                users.Add(email.Email);
                            }
                        }
                    }
                }

                var not = session.Notifications.Single(n => n.Id == (int)NotificationTypes.IdeaResolved);
                notification.MessageTemplate = not.Message;
                notification.Subject = not.Subject;
                notification.ObjectLink = link;

            }

            foreach (var mailUser in users)
            {
                notification.To = mailUser;
                notification.Execute();
            }
        }

        public void SendObjectCreatedNotification(ObjectCreatedNotification notification, NotificationTypes type, string objectId, string absoluteObjectLink)
        {
            using (var session = actionsSessionFactory.CreateContext())
            {
                int[] actionTypes;
                if (type == NotificationTypes.IdeaCreated)
                {
                    actionTypes = new[] { (int)ActionTypes.IdeaCreated, (int)ActionTypes.IdeaEdited };
                }
                else if (type == NotificationTypes.IssueCreated)
                {
                    actionTypes = new[] { (int)ActionTypes.IssueCreated, (int)ActionTypes.IssueEdited };
                }
                else
                {
                    return;
                }

                var action =
                    session.Actions.FirstOrDefault(
                        a => a.ObjectId == objectId && actionTypes.Contains(a.ActionTypeId));
                if (action != null)
                {
                    SendObjectCreatedNotification(notification, action, type, absoluteObjectLink);
                }
            }
        }

        public void SendObjectCreatedNotification(ObjectCreatedNotification notification, Data.EF.Actions.Action action, NotificationTypes type, string absoluteObjectLink)
        {
            using (var userSession = usersSessionFactory.CreateContext())
            {
                var users = GetUsersToNotify(action);
                if (users.Any())
                {
                    string subject = action.Subject;
                    using (var session = noSqlSessionFactory())
                    {
                        if (type == NotificationTypes.IdeaCreated)
                        {
                            var idea = session.GetAll<Data.MongoDB.Idea>().SingleOrDefault(i => i.Id == action.ObjectId);
                            if (idea != null)
                            {
                                subject = idea.Subject;
                                idea.IsMailSent = true;
                                session.Update(idea);
                            }
                        }
                        else if (type == NotificationTypes.IssueCreated)
                        {
                            var issue =
                                session.GetAll<Data.MongoDB.Issue>().SingleOrDefault(i => i.Id == action.ObjectId);
                            if (issue != null)
                            {
                                subject = issue.Subject;
                                issue.IsMailSent = true;
                                session.Update(issue);
                            }
                        }
                    }
                    notification.ObjectSubject = subject;
                    notification.ObjectLink = absoluteObjectLink;
                    var not = GetNotification((int)type);
                    notification.MessageTemplate = not.Message;
                    notification.Subject = not.Subject + ": " + subject;
                    logger.Information(string.Format("Sending emails for {0} users", users.Count));
                    foreach (
                        var user in users.SelectMany(u => u.UserEmails.Where(e => e.IsEmailConfirmed && e.SendMail)))
                    {
                        notification.To = user.Email;
                        notification.ToDisplayName = user.User.FirstName + " " + user.User.LastName;
                        notification.Execute();
                    }
                }
            }
        }

        public void SendProjectNotification(ProjectNotification notification, ProjectCommand command)
        {
            var users = new List<Data.ViewModels.Project.MemberModel>();
            using (var noSqlSession = noSqlSessionFactory())
            {
                var project =
                    noSqlSession.GetAll<Project>().Where(o => o.Id == command.ProjectId).Single();
                var todo = projectService.GetToDo(project, command.MileStoneId, command.ToDoId);
                var fromUser = noSqlSession.GetAll<Data.MongoDB.User>().SingleOrDefault(u => u.Id == command.UserId);

                if (!string.IsNullOrEmpty(todo.ResponsibleUserId) && todo.ResponsibleUserId != "-1")
                {
                    using (var session = usersSessionFactory.CreateContext())
                    {
                        var dbUser = session.Users.Where(u => u.ObjectId == todo.ResponsibleUserId).Select(u => new {u.FirstName, u.LastName, u.ObjectId, u.UserEmails}).SingleOrDefault();
                        foreach (var email in dbUser.UserEmails.Where(e => e.IsEmailConfirmed && e.SendMail))
                        {
                            users.Add(new Data.ViewModels.Project.MemberModel()
                                {
                                    FullName = dbUser.FirstName + " " + dbUser.LastName,
                                    Email = email.Email,
                                    ObjectId = dbUser.ObjectId
                                });
                        }
                    }
                }
                else
                {
                    users.AddRange(projectService.GetMemberConfirmedEmails(project));
                }

                Data.EF.Users.Notification not = null;
                if (command.ActionType == ActionTypes.ToDoAdded)
                {
                    not = GetNotification((int)NotificationTypes.ProjectTodo);
                }

                if (command.ActionType == ActionTypes.ToDoCommentAdded)
                {
                    not = GetNotification((int)NotificationTypes.ProjectComment);
                    users.AddRange(projectService.GetCommentUserEmails(todo));
                }

                users = users.Where(u => !string.IsNullOrEmpty(u.Email)).Distinct().ToList();

                if (not == null)
                {
                    logger.Information(string.Format("{0} id={1} failed sending mail, notification not found",
                                                     command.GetType().Name, command.MessageId));
                    return;
                }

                if (users.Any())
                {
                    logger.Information(string.Format("Sending emails to {0} users", users.Count));
                }
                else
                {
                    logger.Information(string.Format("{0} id={1} successfully completed, no users to send mail to", command.GetType().Name, command.MessageId));
                    return;
                }

                notification.UserFullName = command.UserFullName;
                notification.UserLink = command.UserLink;
                notification.ProjectLink = command.Link;
                notification.ProjectName = command.ProjectSubject;
                notification.TodoLink = command.ToDoLink;
                notification.ToDoSubject = command.ToDoSubject;
                notification.Text = command.Text.NewLineToHtml();

                notification.MessageTemplate = not.Message;
                notification.Subject = not.Subject;
                if (fromUser != null)
                {
                    notification.FromDisplayName = command.UserFullName;
                    notification.From = fromUser.Email;
                }

                foreach (var user in users)
                {
                    notification.To = user.Email;
                    notification.ToDisplayName = user.FullName;
                    notification.Execute();
                }
            }
        }

        public void SendOrganizationProjectNotification(ProjectNotification notification, OrganizationProjectCommand command)
        {
            var users = new List<Data.ViewModels.Organization.MemberModel>();
            using (var noSqlSession = noSqlSessionFactory())
            {
                var org =
                    noSqlSession.GetAll<Organization>().Where(o => o.Id == command.ObjectId).Select(
                        o => new { Projects = o.Projects, Name = o.Name }).Single();
                var project = org.Projects.Where(p => p.Id == command.ProjectId).Single();
                var todo = project.ToDos.Where(t => t.Id == command.ToDoId).SingleOrDefault();
                var fromUser = noSqlSession.GetAll<Data.MongoDB.User>().SingleOrDefault(u => u.Id == command.UserId);

                if (!string.IsNullOrEmpty(todo.ResponsibleUserId))
                {
                    using (var userSession = usersSessionFactory.CreateContext())
                    {
                        var dbUser = organizationService.GetDbUser(todo.ResponsibleUserId);
                        foreach (var email in userSession.UserEmails.Where(e => e.User.ObjectId == todo.ResponsibleUserId && e.IsEmailConfirmed && e.SendMail))
                        {
                            users.Add(new Data.ViewModels.Organization.MemberModel()
                                {
                                    FullName = dbUser.FirstName + " " + dbUser.LastName,
                                    Email = email.Email,
                                    ObjectId = dbUser.ObjectId
                                });
                        }
                    }
                }
                else
                {
                    users.AddRange(organizationService.GetMemberConfirmedEmails(command.OrganizationId));
                }

                Data.EF.Users.Notification not = null;
                if (command.ActionType == ActionTypes.OrganizationToDoAdded)
                {
                    not = GetNotification((int)NotificationTypes.OrganizationTodo);
                }

                if (command.ActionType == ActionTypes.OrganizationToDoCommentAdded)
                {
                    not = GetNotification((int)NotificationTypes.OrganizationProjectComment);
                    users.AddRange(organizationService.GetCommentUserEmails(todo));
                }

                users = users.Where(u => !string.IsNullOrEmpty(u.Email)).Distinct().ToList();

                if (notification == null)
                {
                    logger.Information(string.Format("{0} id={1} failed sending mail, notification not found",
                                                     command.GetType().Name, command.MessageId));
                    return;
                }

                if (users.Any())
                {
                    logger.Information(string.Format("Sending emails to {0} users", users.Count));
                }
                else
                {
                    logger.Information(string.Format("{0} id={1} successfully completed, no users to send mail to", command.GetType().Name, command.MessageId));
                    return;
                }

                notification.UserFullName = command.UserFullName;
                notification.UserLink = command.UserLink;
                notification.OrganizationLink = command.OrganizationLink;
                notification.OrganizationName = command.OrganizationName;
                notification.ProjectLink = command.Link;
                notification.ProjectName = command.ProjectSubject;
                notification.TodoLink = command.ToDoLink;
                notification.ToDoSubject = command.ToDoSubject;
                notification.Text = command.Text.NewLineToHtml();
                if (fromUser != null)
                {
                    notification.FromDisplayName = command.UserFullName;
                    notification.From = fromUser.Email;
                }

                notification.MessageTemplate = not.Message;
                notification.Subject = not.Subject;
                foreach (var user in users)
                {
                    notification.To = user.Email;
                    notification.ToDisplayName = user.FullName;
                    notification.Execute();
                }
            }
        }

        public void SendPrivateMessage(PrivateMessage message, SendPrivateMessageCommand command, string defaultReplyTo)
        {
            if (string.IsNullOrEmpty(command.Message))
            {
                return;
            }

            using (var session = usersSessionFactory.CreateContext())
            {
                var user = session.Users.SingleOrDefault(u => u.Id == command.UserDbId);
                var userFrom = session.Users.SingleOrDefault(u => u.Id == command.UserFromDbId);

                if (userFrom != null)
                {
                    var from = userFrom.UserEmails.FirstOrDefault(e => e.IsEmailConfirmed && e.SendMail);
                    if (from == null)
                    {
                        from = userFrom.UserEmails.FirstOrDefault(e => e.IsEmailConfirmed);
                    }

                    if (from == null)
                    {
                        from = userFrom.UserEmails.FirstOrDefault();
                    }

                    if (from != null)
                    {
                        message.From = from.Email;
                    }

                    message.FromDisplayName = userFrom.FirstName + " " + userFrom.LastName;
                }

                if (string.IsNullOrEmpty(message.From))
                {
                    if (!string.IsNullOrEmpty(command.AddressFrom))
                    {
                        message.From = command.AddressFrom;
                    }
                    else
                    {
                        message.From = "anonymous@lietuva2.lt";
                    }
                }

                message.Subject = command.Subject;

                if (user == null)
                {
                    message.To = defaultReplyTo;
                    message.Execute(command.Message.NewLineToHtml());
                }
                else
                {
                    message.ToDisplayName = user.FirstName + " " + user.LastName;
                    foreach (var email in user.UserEmails)
                    {
                        message.To = email.Email;
                        message.Execute(command.Message.NewLineToHtml());
                    }
                }
            }
        }

        private List<Data.EF.Users.User> GetUsersToNotify(Data.EF.Actions.Action action)
        {
            List<int> ids;
            using (var session = actionsSessionFactory.CreateContext())
            {
                ids = session.Notifications.Where(n => n.Action.ObjectId == action.ObjectId).Select(n => n.UserId).ToList();
                ids = ids.Except(session.Actions.Where(a => a.ObjectId == action.ObjectId).Select(a => a.UserId).ToList()).ToList();
            }

            using (var session = usersSessionFactory.CreateContext())
            {
                return session.Users.Where(u => ids.Contains(u.Id) && u.UserEmails.Any(e => e.SendMail && e.IsEmailConfirmed)).ToList();
            }
        }

        public void SendUserConfirmedNotification(UserConfirmedNotification notification, UserConfirmedCommand command)
        {
            using (var session = usersSessionFactory.CreateContext(true))
            {
                var user = session.Users.Single(u => u.Id == command.UserId);

                notification.ToDisplayName = user.FirstName + " " + user.LastName;
                int type = command.PersonCodeStatus == 1 ? (int)NotificationTypes.UserConfirmed : (int)NotificationTypes.UserNotConfirmed;
                var not = session.Notifications.Single(n => n.Id == type);
                notification.MessageTemplate = not.Message;
                notification.Subject = not.Subject;
                foreach (var email in user.UserEmails)
                {
                    notification.To = email.Email;
                    notification.Execute();
                }
            }
        }
    }
}