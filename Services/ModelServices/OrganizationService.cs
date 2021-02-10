using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity.Core;
using System.Linq;
using System.Net.Mime;
using System.Transactions;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using Bus.Commands;
using Data.EF.Actions;
using Data.EF.Users;
using Data.Enums;
using Data.Infrastructure.Sessions;
using Data.MongoDB;
using Data.ViewModels.Base;
using Data.ViewModels.Organization;
using Data.ViewModels.Organization.Project;
using EntityFramework.Extensions;
using Framework;
using Framework.Bus;
using Framework.Data;
using Framework.Drawing;
using Framework.Enums;
using Framework.Infrastructure;
using Framework.Mvc.Helpers;
using Framework.Infrastructure.Storage;
using Framework.Infrastructure.ValueInjections;
using Framework.Mvc.Strings;
using Framework.Strings;
using Globalization.Resources.Services;
using MongoDB.Bson;

using Ninject;
using Omu.ValueInjecter;
using Services.Classes;
using Services.Enums;
using Services.Session;
using User = Data.EF.Users.User;
using MongoDB.Driver.Builders;
using OrganizationProjectStates = Data.Enums.OrganizationProjectStates;

namespace Services.ModelServices
{
    public class OrganizationService : IService
    {
        private Func<INoSqlSession> noSqlSessionFactory;
        private IActionsContextFactory actionSessionFactory;
        private IUsersContextFactory userSessionFactory;

        public NewsFeedService NewsFeedService { get { return ServiceLocator.Resolve<NewsFeedService>(); } }
        public IdeaService IdeaService { get { return ServiceLocator.Resolve<IdeaService>(); } }
        public VotingService VotingService { get { return ServiceLocator.Resolve<VotingService>(); } }
        public UserService UserService { get { return ServiceLocator.Resolve<UserService>(); } }
        public ShortLinkService ShortLinkService { get { return ServiceLocator.Resolve<ShortLinkService>(); } }
        public ProblemService ProblemService { get { return ServiceLocator.Resolve<ProblemService>(); } }

        private readonly IBus bus;

        public UserInfo CurrentUser { get { return MembershipSession.GetUser(); } }

        public UrlHelper Url
        {
            get { return new UrlHelper(((MvcHandler)HttpContext.Current.Handler).RequestContext); }
        }

        public OrganizationService(
            Func<INoSqlSession> mongoDbSessionFactory,
            IActionsContextFactory actionSessionFactory,
            IUsersContextFactory userSessionFactory,
            IBus bus)
        {
            this.noSqlSessionFactory = mongoDbSessionFactory;
            this.actionSessionFactory = actionSessionFactory;
            this.userSessionFactory = userSessionFactory;
            this.bus = bus;
        }

        public OrganizationViewModel Create(OrganizationCreateModel model)
        {
            var organization = new Organization()
                                   {
                                       Name = model.Name,
                                       Type = model.Type,
                                       CreatedByUserObjectId = CurrentUser.Id
                                   };

            organization.ShortLink = GetShortLink(organization);

            using (var session = noSqlSessionFactory())
            {
                session.Add(organization);
            }

            using (var actionSession = actionSessionFactory.CreateContext())
            {
                var uo = new UserInterestingOrganization()
                             {
                                 IsMember = true,
                                 IsConfirmed = true,
                                 UserId = CurrentUser.DbId.Value,
                                 OrganizationId = organization.Id,
                                 Permission = (int)UserRoles.Admin,
                                 VoteCount = 1
                             };
                actionSession.UserInterestingOrganizations.Add(uo);

                try
                {
                    actionSession.SaveChanges();
                }
                catch (Exception e)
                {
                    using (var session = noSqlSessionFactory())
                    {
                        session.Delete(organization);
                    }

                    throw;
                }

                var orgs = UserService.GetCurrentUserOrganizations();
                orgs.Add(new UserOrganization()
                                                  {
                                                      IsMember = true,
                                                      OrganizationId = organization.Id,
                                                      OrganizationName = organization.Name,
                                                      Permission = UserRoles.Admin
                                                  });
            }

            bus.Send(new OrganizationCommand()
                         {
                             ActionType = ActionTypes.OrganizationCreated,
                             ObjectId = organization.Id,
                             UserId = CurrentUser.Id
                         });

            return new OrganizationViewModel()
                       {
                           Name = model.Name,
                           ObjectId = organization.Id
                       };
        }

        public OrganizationCreateModel Create()
        {
            return new OrganizationCreateModel()
                       {
                           Types = GetOrganizationTypes()
                       };
        }

        public OrganizationViewModel GetOrganizationModel(MongoObjectId objectId, OrganizationViews view, IdeaListSorts? ideaSort, IssueListSorts? issueSort, ProblemListSorts? problemSort)
        {
            Data.MongoDB.Organization org = GetOrganization(objectId);
            if (org == null)
            {
                throw new ObjectNotFoundException();
            }

            return GetOrganizationModel(org, view, ideaSort, issueSort, problemSort);
        }

        public OrganizationViewModel GetOrganizationModel(Data.MongoDB.Organization organization, OrganizationViews view, IdeaListSorts? ideaSort, IssueListSorts? issueSort, ProblemListSorts? problemSort)
        {
            var uo = GetUserInterestingOrganization(organization.Id);
            var org = new OrganizationViewModel
                          {
                              Name = organization.Name,
                              ObjectId = organization.Id,
                              HasProfilePicture = organization.ProfilePictureId != null,
                              View = view
                          };



            var membersCount = GetConfirmedOrganizationMembersCount(organization.Id);
            org.IsLikeable = uo == null && CurrentUser.IsAuthenticated;
            org.IsUnlikeable = uo != null && !uo.IsMember;
            org.IsJoinable = (uo == null || !uo.IsMember);
            org.IsLeavable = uo != null && uo.IsMember && (!uo.IsConfirmed || membersCount > 1);
            org.IsEditable = GetIsEditable(uo);
            org.IsContributable = GetIsContributable(uo);
            org.IsDeletable = CurrentUser.Role == UserRoles.Admin || (org.IsEditable && membersCount == 1) || (org.IsEditable && organization.CreatedByUserObjectId == CurrentUser.Id);
            org.WaitingForApprove = uo != null && uo.IsMember && !uo.IsConfirmed;

            if (org.IsLeavable && !CurrentUser.IsUserInOrganization(organization.Id))
            {
                UserService.ResetCurrentUserOrganizations();
            }

            org.Info = GetInfo(organization);
            org.Contacts = GetContacts(organization);
            org.Info.IsEditable = org.IsEditable;
            org.Contacts.IsEditable = org.IsEditable;
            org.ShortLink = organization.ShortLink ?? string.Empty;
            org.SupportersCount = GetOrganizationSupportersCount(organization.Id);

            if (view == OrganizationViews.Activity)
            {
                org.ActivityList = NewsFeedService.GetOrganizationActivityPage(organization.Id, 0);
            }

            if (view == OrganizationViews.Members)
            {
                var members = GetOrganizationMembers(organization.Id);
                org.MembersCount = members.Count(m => m.IsConfirmed);
                org.Members = (from m in members
                               where m.IsConfirmed && (!m.IsPrivate || org.IsContributable)
                               let u = GetUser(m.UserId)
                               let iu = m.InvitedByUserId.HasValue ? GetUser(m.InvitedByUserId.Value) : null
                               select new MemberModel()
                                          {
                                              FullName = u.FullName,
                                              ObjectId = u.Id,
                                              DbId = u.DbId,
                                              Role = m.Role,
                                              IsEditable = org.IsEditable && (u.Id != organization.CreatedByUserObjectId || CurrentUser.Id == organization.CreatedByUserObjectId),
                                              OrganizationId = org.ObjectId,
                                              IsPublic = !m.IsPrivate,
                                              IsCurrentUser = u.Id == CurrentUser.Id,
                                              Permission = (UserRoles)m.Permission,
                                              InvitedBy = iu != null ? iu.FullName : string.Empty
                                          }).ToList();
                org.UnconfirmedMembers = (from m in members
                                          where !m.IsConfirmed
                                          let u = GetUser(m.UserId)
                                          select new UserLinkModel()
                                                     {
                                                         FullName = u.FullName,
                                                         ObjectId = u.Id
                                                     }).ToList();
                org.CustomInvitationText = organization.CustomInvitationText;
                using (var userSession = userSessionFactory.CreateContext())
                {
                    string orgId = organization.Id;
                    org.InvitedUsers =
                        userSession.UserInvitations.Where(
                            ui => ui.OrganizationId == orgId && !ui.Joined)
                            .Select(ui => new InviteUserModel()
                                              {
                                                  InvitedUser = ui.UserEmail,
                                                  InvitationSent = true,
                                                  Message = ui.Message
                                              }).ToList();
                }

            }

            if (view == OrganizationViews.Projects)
            {
                org.Projects = GetProjectList(organization, org.IsContributable, ProjectListViews.Active);
            }

            if (view == OrganizationViews.Ideas)
            {
                org.Ideas = IdeaService.GetIdeaPage(0, null, ideaSort ?? IdeaListSorts.MostRecent, null, organization.Id, null);
                org.Ideas.OrganizationId = organization.Id;
                org.Ideas.IdeaListSort = (int)(ideaSort ?? IdeaListSorts.MostRecent);
                org.Ideas.IsEditable = org.IsContributable;
            }

            if (view == OrganizationViews.Issues)
            {
                org.Issues = VotingService.GetIssuePage(0, null, issueSort ?? IssueListSorts.Nearest, null, organization.Id);
                org.Issues.OrganizationId = organization.Id;
                org.Issues.OrganizationName = organization.Name;
                org.Issues.ListSort = (int)(issueSort ?? IssueListSorts.Nearest);
                org.Issues.IsEditable = org.IsContributable;
            }

            if (view == OrganizationViews.Results)
            {
                org.Results = VotingService.GetResultsPage(0, null, issueSort ?? IssueListSorts.Nearest, null, organization.Id);
                org.Results.OrganizationId = organization.Id;
                org.Results.OrganizationName = organization.Name;
                org.Results.ListSort = (int)(issueSort ?? IssueListSorts.Nearest);
                org.Results.IsEditable = org.IsContributable;
            }

            if (view == OrganizationViews.Problems)
            {
                org.Problems = ProblemService.GetProblemPage(0, null, problemSort ?? ProblemListSorts.Newest, null, organization.Id, null);
                org.Problems.OrganizationId = organization.Id;
                org.Problems.OrganizationName = organization.Name;
                org.Problems.ListSort = (int)(problemSort ?? ProblemListSorts.Newest);
                org.Problems.IsEditable = org.IsContributable;
            }

            if (CurrentUser.IsAuthenticated && view == OrganizationViews.Info)
            {
                bus.Send(new OrganizationCommand()
                             {
                                 ActionType = ActionTypes.OrganizationViewed,
                                 UserId = CurrentUser.Id,
                                 ObjectId = organization.Id,
                                 IsPrivate = true
                             });
            }

            return org;
        }

        public void UpdateShortLinks()
        {
            using (var session = noSqlSessionFactory())
            {
                foreach (var org in session.GetAll<Data.MongoDB.Organization>())
                {
                    org.ShortLink = GetShortLink(org);
                    session.Update(org);
                }
            }
        }

        private string GetShortLink(Data.MongoDB.Organization org)
        {
            return ShortLinkService.GetShortLink(org.ShortLink ?? org.Name,
                                                 GetDetailsUrl(org));
        }

        private string GetDetailsUrl(Data.MongoDB.Organization org)
        {
            return Url.Action("Details", "Organization",
                                      new { objectId = org.Id, name = org.Name.ToSeoUrl() });
        }

        public List<MemberModel> GetConfirmedMembers(string organizationId)
        {
            return GetMembers(GetConfirmedOrganizationMembers(organizationId));
        }

        public List<MemberModel> GetMemberConfirmedEmails(string organizationId)
        {
            using (var session = userSessionFactory.CreateContext())
            {
                return (from m in GetConfirmedOrganizationMembers(organizationId)
                        from u in session.UserEmails
                        where u.UserId == m.UserId && u.IsEmailConfirmed && u.SendMail
                        select new MemberModel()
                        {
                            FullName = u.User.FirstName + " " + u.User.LastName,
                            ObjectId = u.User.ObjectId,
                            Email = u.Email
                        }).ToList();
            }
        }

        private List<MemberModel> GetMembers(List<UserInterestingOrganization> members)
        {
            return (from m in members
                    where m.IsConfirmed
                    let u = GetUser(m.UserId)
                    select new MemberModel()
                               {
                                   DbId = m.UserId,
                                   FullName = u.FullName,
                                   ObjectId = u.Id,
                                   Role = m.Role,
                                   Permission = (UserRoles)m.Permission,
                                   IsPublic = !m.IsPrivate,
                                   IsCurrentUser = m.UserId == CurrentUser.DbId,
                                   VoteCount = m.VoteCount
                               }).ToList();
        }

        public ContactsViewModel GetContacts(MongoObjectId organizationId)
        {
            var user = GetOrganization(organizationId);
            return GetContacts(user);
        }

        public InfoViewModel GetInfo(MongoObjectId organizationId)
        {
            var org = GetOrganization(organizationId);
            return GetInfo(org);
        }

        public InfoViewModel SaveInfo(InfoEditModel personalInfo)
        {
            var org = GetOrganization(personalInfo.ObjectId);
            org.InjectFrom<UniversalInjection>(personalInfo);
            org.Type = (Data.MongoDB.OrganizationTypes)personalInfo.Type;

            UpdateOrganization(org);

            bus.Send(new OrganizationCommand()
                         {
                             ActionType = ActionTypes.OrganizationUpdated,
                             UserId = CurrentUser.Id,
                             ObjectId = org.Id
                         });

            var result = GetInfo(org);
            result.IsEditable = true;
            return result;
        }

        public ContactsViewModel SaveContacts(ContactsEditModel model)
        {
            var organization = GetOrganization(model.ObjectId);

            organization.InjectFrom<UniversalInjection>(model);

            model.WebSites.BindUrls(organization.WebSites);

            UpdateOrganization(organization);

            bus.Send(new OrganizationCommand()
                         {
                             ActionType = ActionTypes.OrganizationUpdated,
                             UserId = CurrentUser.Id,
                             ObjectId = organization.Id
                         });

            var result = GetContacts(organization);
            result.IsEditable = true;
            return result;
        }

        public InfoEditModel GetInfoForEdit(MongoObjectId organizationId)
        {
            var organization = GetOrganization(organizationId);
            return new InfoEditModel()
                       {
                           Description = organization.Description,
                           FoundedOn = organization.FoundedOn,
                           Goals = organization.Goals,
                           Mission = organization.Mission,
                           Name = organization.Name,
                           ObjectId = organizationId,
                           Type = (int)organization.Type,
                           Vision = organization.Vision,
                           Types = GetOrganizationTypes()
                       };
        }

        public ContactsEditModel GetContactsForEdit(MongoObjectId organizationId)
        {
            var organization = GetOrganization(organizationId);
            var model = new ContactsEditModel();
            model.InjectFrom(organization);
            model.ObjectId = organizationId;
            foreach (var item in organization.WebSites)
            {
                var e = new UrlEditModel("WebSites");
                e.InjectFrom<UniversalInjection>(item);
                model.WebSites.Add(e);
            }
            if (model.WebSites.Count == 0)
            {
                var wsModel = new UrlEditModel("WebSites");
                model.WebSites.Add(wsModel);
            }

            return model;
        }

        public FileViewModel GetProfilePicture(MongoObjectId organizationId)
        {
            var imageId = GetOrganization(organizationId).ProfilePictureId;
            using (var noSqlSession = noSqlSessionFactory())
            {
                return noSqlSession.GetFile<Data.MongoDB.Organization>(imageId);
            }
        }

        public string GetProfilePictureThumbId(MongoObjectId userObjectId)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                if (userObjectId == null)
                {
                    return null;
                }

                var user = noSqlSession.Find<Data.MongoDB.Organization>(Query.EQ("_id", userObjectId)).SetFields("ProfilePictureThumbId").SingleOrDefault();
                if (user != null)
                {
                    return user.ProfilePictureThumbId;
                }

                return null;
            }
        }

        public FileViewModel GetProfilePictureThumb(MongoObjectId organizationId)
        {
            var imageId = GetOrganization(organizationId).ProfilePictureThumbId;
            if (imageId == null)
            {
                return null;
            }

            using (var noSqlSession = noSqlSessionFactory())
            {
                return noSqlSession.GetFile<Data.MongoDB.Organization>(imageId);
            }
        }

        public void SaveProfilePicture(MongoObjectId organizationId, byte[] file, string contentType)
        {
            var organization = GetOrganization(organizationId);
            var resizedPicture = PictureProcessor.ResizeImageFile(file, 200);
            var thumb = PictureProcessor.ResizeImageFile(file, 50);
            using (var noSqlSession = noSqlSessionFactory())
            {
                var id = noSqlSession.SaveFile("OrganizationLogo_" + organizationId, resizedPicture, contentType);
                organization.ProfilePictureId = id;
                organization.ProfilePictureHistory.Add(id);

                var thumbId = noSqlSession.SaveFile("OrganizationLogoThumb_" + organizationId, thumb, contentType);
                organization.ProfilePictureThumbId = thumbId;
                organization.ProfilePictureHistory.Add(thumbId);
                noSqlSession.Update(organization);
            }

            bus.Send(new OrganizationCommand()
                         {
                             ActionType = ActionTypes.OrganizationUpdated,
                             UserId = CurrentUser.Id,
                             ObjectId = organizationId
                         });
        }

        public void Like(string objectId)
        {
            using (var actionSession = actionSessionFactory.CreateContext(true))
            {
                var uo = new UserInterestingOrganization()
                             {
                                 OrganizationId = objectId,
                                 UserId = CurrentUser.DbId.Value,
                                 IsMember = false
                             };
                actionSession.UserInterestingOrganizations.Add(uo);
            }

            bus.Send(new OrganizationCommand()
                         {
                             ActionType = ActionTypes.OrganizationLiked,
                             UserId = CurrentUser.Id,
                             ObjectId = objectId
                         });
        }

        public void Unlike(string objectId)
        {
            using (var actionSession = actionSessionFactory.CreateContext(true))
            {
                actionSession.UserInterestingOrganizations.Delete(a => a.UserId == CurrentUser.DbId && a.OrganizationId == objectId);
            }
        }

        public void Join(string objectId)
        {
            using (var actionSession = actionSessionFactory.CreateContext(true))
            {
                var uo =
                    actionSession.UserInterestingOrganizations.Where(
                        u => u.OrganizationId == objectId && u.UserId == CurrentUser.DbId).SingleOrDefault();
                if (uo == null)
                {
                    uo = new UserInterestingOrganization()
                             {
                                 OrganizationId = objectId,
                                 UserId = CurrentUser.DbId.Value
                             };
                    actionSession.UserInterestingOrganizations.Add(uo);
                }

                uo.IsMember = true;
                uo.IsConfirmed = false;
            }

            bus.Send(new OrganizationCommand()
                         {
                             ActionType = ActionTypes.OrganizationJoining,
                             UserId = CurrentUser.Id,
                             ObjectId = objectId
                         });
        }

        public void Leave(string objectId)
        {
            using (var actionSession = actionSessionFactory.CreateContext(true))
            {
                var uo =
                    actionSession.UserInterestingOrganizations.Where(
                        u => u.OrganizationId == objectId && u.UserId == CurrentUser.DbId).SingleOrDefault();
                uo.IsMember = false;
            }
        }

        public bool Confirm(string organizationId, string userObjectId)
        {
            int userDbId;
            using (var session = noSqlSessionFactory())
            {
                userDbId =
                    session.GetAll<Data.MongoDB.User>().Where(u => u.Id == userObjectId).Select(u => u.DbId).
                        Single();
            }

            using (var actionSession = actionSessionFactory.CreateContext(true))
            {
                var uo = GetUserInterestingOrganization(organizationId, userDbId);
                uo.IsConfirmed = true;
                uo.InvitedByUserId = CurrentUser.DbId;
            }

            bus.Send(new OrganizationCommand()
                         {
                             ActionType = ActionTypes.OrganizationJoined,
                             UserId = userObjectId,
                             ObjectId = organizationId
                         });

            return true;
        }

        public bool Reject(string organizationId, string userObjectId)
        {
            int userDbId;
            using (var session = noSqlSessionFactory())
            {
                userDbId =
                    session.GetAll<Data.MongoDB.User>().Where(u => u.Id == userObjectId).Select(u => u.DbId).
                        Single();
            }

            using (var actionSession = actionSessionFactory.CreateContext(true))
            {
                var uo = GetUserInterestingOrganization(organizationId, userDbId);
                uo.IsConfirmed = false;
                uo.IsMember = false;
                uo.InvitedByUserId = null;
            }

            bus.Send(new OrganizationMemberAddCommand()
            {
                ActionType = ActionTypes.OrganizationMemberRejected,
                ObjectId = organizationId,
                UserId = CurrentUser.Id,
                AddedUserId = userDbId
            });

            return true;
        }

        private Data.EF.Actions.UserInterestingOrganization GetUserInterestingOrganization(string organizationId)
        {
            if (!CurrentUser.IsAuthenticated)
            {
                return null;
            }

            return GetUserInterestingOrganization(organizationId, CurrentUser.DbId.Value);
        }

        private Data.EF.Actions.UserInterestingOrganization GetUserInterestingOrganization(string organizationId,
                                                                                           int userId)
        {
            using (var session = actionSessionFactory.CreateContext())
            {
                var q = from u in session.UserInterestingOrganizations
                        where u.UserId == userId && u.OrganizationId == organizationId
                        select u;

                return q.SingleOrDefault();
            }
        }

        private InfoViewModel GetInfo(Data.MongoDB.Organization organization)
        {
            var personalInfo = new InfoViewModel()
                                   {
                                       Description = organization.Description.NewLineToHtml(),
                                       FoundedOn =
                                           organization.FoundedOn.HasValue
                                               ? organization.FoundedOn.Value.ToShortDateString()
                                               : string.Empty,
                                       Goals = organization.Goals.NewLineToHtml(),
                                       Mission = organization.Mission.NewLineToHtml(),
                                       Name = organization.Name,
                                       ObjectId = organization.Id,
                                       Type =
                                           Globalization.Resources.Services.OrganizationTypes.ResourceManager.GetString(
                                               organization.Type.ToString()),
                                       Vision = organization.Vision.NewLineToHtml(),
                                       IsFilled = true
                                   };

            return personalInfo;
        }

        private ContactsViewModel GetContacts(Data.MongoDB.Organization organization)
        {
            var model = new ContactsViewModel();
            model.InjectFrom(organization);
            model.ObjectId = organization.Id;
            model.WebSites = organization.WebSites.Select(w => new UrlViewModel
                                                                   {
                                                                       Title = w.Title,
                                                                       Url = w.Url
                                                                   }).ToList();
            model.IsFilled = organization.WebSites.Count > 0 || !string.IsNullOrEmpty(organization.Address) ||
                             !string.IsNullOrEmpty(organization.PhoneNumber) ||
                             !string.IsNullOrEmpty(organization.Email);
            return model;
        }

        private void UpdateOrganization(Data.MongoDB.Organization org)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                noSqlSession.Update(org);
            }
        }

        private Data.MongoDB.Organization GetOrganization(MongoObjectId organizationId)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                return noSqlSession.GetById<Data.MongoDB.Organization>(organizationId);
            }
        }

        private List<SelectListItem> GetOrganizationTypes()
        {
            var types = Enum.GetNames(typeof(Data.MongoDB.OrganizationTypes));

            return types.Select(t => new SelectListItem()
                                         {
                                             Text =
                                                 Globalization.Resources.Services.OrganizationTypes.ResourceManager.
                                                 GetString(t),
                                             Value =
                                                 ((int)Enum.Parse(typeof(Data.MongoDB.OrganizationTypes), t)).ToString
                                                 ()
                                         }).ToList();
        }

        private List<UserInterestingOrganization> GetOrganizationMembers(string organizationId)
        {
            using (var session = actionSessionFactory.CreateContext())
            {
                return
                    session.UserInterestingOrganizations.Where(
                        u => u.OrganizationId == organizationId && u.IsMember).ToList();
            }
        }

        private List<UserInterestingOrganization> GetConfirmedOrganizationMembers(string organizationId)
        {
            using (var session = actionSessionFactory.CreateContext())
            {
                return
                    session.UserInterestingOrganizations.Where(
                        u => u.OrganizationId == organizationId && u.IsMember && u.IsConfirmed).ToList();
            }
        }

        private int GetConfirmedOrganizationMembersCount(string organizationId)
        {
            using (var session = actionSessionFactory.CreateContext())
            {
                return
                    session.UserInterestingOrganizations.Count(u => u.OrganizationId == organizationId && u.IsMember && u.IsConfirmed);
            }
        }

        private int GetOrganizationSupportersCount(string organizationId)
        {
            using (var session = actionSessionFactory.CreateContext())
            {
                return
                    session.UserInterestingOrganizations.Count(u => u.OrganizationId == organizationId);
            }
        }

        private Data.MongoDB.User GetUser(int userDbId)
        {
            using (var session = noSqlSessionFactory())
            {
                return
                    session.Find<Data.MongoDB.User>(Query.EQ("DbId", userDbId)).SetFields("FirstName", "LastName", "Id", "DbId")
                        .SingleOrDefault();
            }
        }

        public Data.EF.Users.User GetDbUser(int userDbId)
        {
            using (var session = userSessionFactory.CreateContext())
            {
                return
                    session.Users.SingleOrDefault(u => u.Id == userDbId) ?? new Data.EF.Users.User();
            }
        }

        public Data.EF.Users.User GetDbUser(string userObjectId)
        {
            using (var session = userSessionFactory.CreateContext())
            {
                return
                    session.Users.SingleOrDefault(u => u.ObjectId == userObjectId);
            }
        }

        public List<UserEmail> GetDbUserEmails(string userObjectId)
        {
            using (var session = userSessionFactory.CreateContext())
            {
                return
                    session.Users.Where(u => u.ObjectId == userObjectId).SelectMany(u => u.UserEmails).ToList();
            }
        }

        public List<MemberModel> SaveMembers(OrganizationViewModel model)
        {
            using (var session = actionSessionFactory.CreateContext(true))
            {
                var members =
                    session.UserInterestingOrganizations.Where(
                        u => u.OrganizationId == model.ObjectId && u.IsMember && u.IsConfirmed).ToList();
                foreach (var member in model.Members)
                {
                    var m = members.Where(me => me.UserId == member.DbId).Single();
                    m.Role = member.Role;
                    m.Permission = (int)member.Permission;
                    m.IsPrivate = !member.IsPublic;
                    m.VoteCount = member.VoteCount;
                }
            }

            bus.Send(new OrganizationCommand()
                         {
                             ActionType = ActionTypes.OrganizationMemberRolesUpdated,
                             ObjectId = model.ObjectId,
                             UserId = CurrentUser.Id
                         });

            return GetConfirmedMembers(model.ObjectId);
        }

        /*******************************Projects******************************/

        public ProjectViewModel AddProject(string organizationId, string name, bool isPrivate)
        {
            var project = new OrganizationProject()
                              {
                                  Id = BsonObjectId.GenerateNewId(),
                                  IsPrivate = isPrivate,
                                  Subject = name,
                                  ModificationDate = DateTime.Now
                              };
            var org = GetOrganization(organizationId);
            org.Projects.Add(project);
            UpdateOrganization(org);

            bus.Send(new OrganizationProjectCommand()
                         {
                             ActionType = ActionTypes.OrganizationProjectAdded,
                             OrganizationId = organizationId,
                             UserId = CurrentUser.Id,
                             ProjectSubject = project.Subject,
                             Link = GetProjectUrl(organizationId, project.Id),
                             ProjectId = project.Id,
                             IsPrivate = isPrivate
                         });

            return GetProjectModelFromOrganzationProject(project, true, organizationId);
        }

        public ProjectToDosModel ToDos(string organizationId, string projectId)
        {
            return ToDos(GetProject(organizationId, projectId));
        }

        public ProjectToDosModel ToDos(OrganizationProject project)
        {
            var model = new ProjectToDosModel()
                            {
                                Id = project.Id,
                                Subject = project.Subject,
                                InsertResponsibleUsers = GetResponsibleUsers(project.Organization.Id),
                                IsEditable = IsOrganizationEditable(project.Organization.Id),
                                OrganizationId = project.Organization.Id,
                                OrganizationName = project.Organization.Name,
                                IsPrivate = project.IsPrivate,
                                InsertToDoIsPrivate = project.IsPrivate,
                                StateDescription = project.StateDescription,
                                State =
                                    Globalization.Resources.Services.OrganizationProjectStates.ResourceManager.GetString
                                    (project.State.ToString())
                            };

            AddEmptyUser(model.InsertResponsibleUsers);

            model.ToDos = FilterToDos(project, project.ToDos, model.IsEditable);
            model.FinishedToDos = FilterFinishedToDos(project, project.ToDos, model.IsEditable);

            return model;
        }

        private bool IsOrganizationEditable(string organizationId)
        {
            var uo = GetUserInterestingOrganization(organizationId);
            return uo != null && uo.IsConfirmed && uo.IsMember;
        }

        private List<ToDoModel> FilterToDos(OrganizationProject project, List<ToDo> todos, bool isEditable)
        {
            return
                todos.Where(
                    td => !td.FinishDate.HasValue && (!td.IsPrivate || isEditable))
                    .OrderBy(td => td.Position).Select(
                        td => GetToDoModelFromToDo(td, project, isEditable)).ToList();
        }

        private List<ToDoModel> FilterFinishedToDos(OrganizationProject project, List<ToDo> todos, bool isEditable)
        {
            return
                todos.Where(td => td.FinishDate.HasValue && (!td.IsPrivate || isEditable))
                    .OrderByDescending(td => td.FinishDate).Select(
                        td => GetToDoModelFromToDo(td, project, isEditable)).ToList();
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

        public ToDoModel AddToDo(ProjectToDosModel model)
        {
            var project = GetProject(model.OrganizationId, model.Id);
            var todo = new ToDo()
                           {
                               Id = BsonObjectId.GenerateNewId(),
                               Subject = model.InsertSubject,
                               ResponsibleUserId = model.InsertResponsibleUserId,
                               DueDate = model.InsertDueDate,
                               Position = project.ToDos.Where(td => !td.FinishDate.HasValue && (!td.IsPrivate || IsOrganizationEditable(model.OrganizationId))).Count(),
                               IsPrivate = model.InsertToDoIsPrivate,
                               CreatedByUserId = CurrentUser.Id
                           };

            project.ToDos.Add(todo);

            project.ModificationDate = DateTime.Now;
            UpdateOrganization(project.Organization);

            bus.Send(new OrganizationProjectCommand()
                         {
                             ActionType = ActionTypes.OrganizationToDoAdded,
                             OrganizationId = model.OrganizationId,
                             UserId = CurrentUser.Id,
                             ToDoId = todo.Id,
                             Text = todo.Subject,
                             ProjectSubject = project.Subject,
                             Link = Url.ActionAbsolute("ToDos", "Organization", new { organizationId = model.OrganizationId, projectId = project.Id }),
                             ProjectId = model.Id,
                             IsPrivate = project.IsPrivate || todo.IsPrivate,
                             SendNotifications = model.InsertSendNotifications,
                             UserFullName = CurrentUser.FullName,
                             UserLink = Url.ActionAbsolute("Details", "Account", new { userObjectId = CurrentUser.Id }),
                             OrganizationName = project.Organization.Name,
                             OrganizationLink = Url.ActionAbsolute("Details", "Organization", new { objectId = project.Organization.Id, name = project.Organization.Name.ToSeoUrl() })
                         });

            return GetToDoModelFromToDo(todo, project, true);
        }

        private ToDoModel GetToDoModelFromToDo(ToDo todo, OrganizationProject project, bool isEditable)
        {
            return new ToDoModel()
                       {
                           OrganizationId = project.Organization.Id,
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
                           CreatedByUserId = todo.CreatedByUserId,
                           CreatedByUserFullName = todo.CreatedByUserId != null ? GetUserFullName(todo.CreatedByUserId) : null
                       };
        }

        public ToDoModel FinishToDo(string organizationId, string projectId, string id)
        {
            var project = GetProject(organizationId, projectId);
            var todo = GetToDo(project, id);
            if (todo.FinishDate.HasValue)
            {
                todo.FinishDate = null;
                todo.Position = GetSortedTodos(project).Count - 1;

                bus.Send(new ToDoUnfinishedCommand()
                             {
                                 ToDoId = todo.Id
                             });
            }
            else
            {
                todo.FinishDate = DateTime.Now;
                todo.Position = null;
                UpdateToDoOrder(project);

                bus.Send(new OrganizationProjectCommand()
                             {
                                 ActionType = ActionTypes.OrganizationToDoFinished,
                                 OrganizationId = organizationId,
                                 ProjectId = project.Id,
                                 UserId = CurrentUser.Id,
                                 ToDoId = todo.Id,
                                 Text = todo.Subject,
                                 ProjectSubject = project.Subject,
                                 Link = GetProjectUrl(project.Organization.Id, project.Id),
                                 IsPrivate = project.IsPrivate || todo.IsPrivate
                             });
            }

            project.ModificationDate = DateTime.Now;
            UpdateOrganization(project.Organization);

            return GetToDoModelFromToDo(todo, project, true);
        }

        public bool DeleteToDo(string organizationId, string projectId, string id)
        {
            var project = GetProject(organizationId, projectId);
            var todo = GetToDo(project, id);
            project.ToDos.Remove(todo);
            UpdateToDoOrder(project);
            project.ModificationDate = DateTime.Now;
            UpdateOrganization(project.Organization);

            bus.Send(new ToDoDeletedCommand()
                         {
                             ToDoId = todo.Id
                         });

            return true;
        }

        public EditToDoModel GetToDoEdit(string organizationId, string projectId, string id)
        {
            var project = GetProject(organizationId, projectId);
            var todo = GetToDo(project, id);
            var model = new EditToDoModel()
                            {
                                Id = todo.Id,
                                ProjectId = project.Id,
                                DueDate = todo.DueDate,
                                ResponsibleUserId = todo.ResponsibleUserId,
                                Subject = todo.Subject,
                                ResponsibleUsers = GetResponsibleUsers(organizationId),
                                IsPrivate = todo.IsPrivate,
                                IsProjectPrivate = project.IsPrivate
                            };

            AddEmptyUser(model.ResponsibleUsers);
            return model;
        }

        private List<SelectListItem> GetResponsibleUsers(string organizationId)
        {
            return (from m in GetOrganizationMembers(organizationId)
                    let user = GetUser(m.UserId)
                    select new SelectListItem()
                               {
                                   Text = user.FullName,
                                   Value = user.Id
                               }).ToList();
        }

        public ToDoModel EditToDo(EditToDoModel model)
        {
            var project = GetProject(model.OrganizationId, model.ProjectId);
            var todo = GetToDo(project, model.Id);
            todo.Subject = model.Subject;
            todo.DueDate = model.DueDate;
            todo.ResponsibleUserId = model.IsPrivate ? CurrentUser.Id : model.ResponsibleUserId;
            todo.IsPrivate = project.IsPrivate || model.IsPrivate;
            project.ModificationDate = DateTime.Now;
            UpdateOrganization(project.Organization);

            bus.Send(new OrganizationProjectCommand()
                         {
                             ActionType = ActionTypes.OrganizationToDoEdited,
                             ProjectId = project.Id,
                             OrganizationId = model.OrganizationId,
                             UserId = CurrentUser.Id,
                             ToDoId = todo.Id,
                             Text = todo.Subject,
                             ProjectSubject = project.Subject,
                             Link = GetProjectUrl(model.OrganizationId, project.Id),
                             IsPrivate = todo.IsPrivate
                         });

            return GetToDoModelFromToDo(todo, project, true);
        }

        public void ReorderToDos(string organizationId, string projectId, int startPos, int endPos)
        {
            var project = GetProject(organizationId, projectId);
            ToDo todo;
            List<ToDo> sortedList;
            int finalPos = -1;
            var list = FilterToDos(project, project.ToDos, true);
            var td = list[startPos];
            todo = project.ToDos.Where(t => t.Id == td.ToDoId).Single();
            sortedList = GetSortedTodos(project);
            finalPos = list[endPos].Position.Value;

            if (finalPos < 0) finalPos = 0;
            sortedList.Remove(todo);
            sortedList.Insert(finalPos, todo);
            UpdateToDoOrder(sortedList);
            project.ModificationDate = DateTime.Now;
            UpdateOrganization(project.Organization);
        }

        private void UpdateToDoOrder(List<ToDo> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                list[i].Position = i;
            }
        }

        private void UpdateToDoOrder(OrganizationProject project)
        {
            UpdateToDoOrder(GetSortedTodos(project));
        }

        private List<ToDo> GetSortedTodos(OrganizationProject project)
        {
            return project.ToDos.Where(t => !t.FinishDate.HasValue).OrderBy(t => t.Position).ToList();
        }

        public CommentsModel GetComments(string organizationId, string projectId, string toDoId)
        {
            var project = GetProject(organizationId, projectId);
            var todo = GetToDo(project, toDoId);
            var isEditable = IsOrganizationEditable(organizationId);
            var model = new CommentsModel()
                            {
                                ProjectName = project.Subject,
                                IsEditable = isEditable,
                                IsTaskPrivate = project.IsPrivate || todo.IsPrivate,
                                InsertIsPrivate = project.IsPrivate,
                                Comments = (from c in todo.Comments
                                            orderby c.Date
                                            where c.IsPrivate == false || isEditable
                                            select GetCommentModelFromComment(c, project, toDoId)).ToList(),
                                ToDo = GetToDoModelFromToDo(todo, project, isEditable)
                            };
            return model;
        }

        public CommentModel AddComment(string organizationId, string projectId, string toDoId, string text,
                                       bool isPrivate, bool sendNotifications, List<UrlViewModel> attachments)
        {
            var project = GetProject(organizationId, projectId);
            var todo = GetToDo(project, toDoId);
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
            project.ModificationDate = DateTime.Now;
            UpdateOrganization(project.Organization);

            bus.Send(new OrganizationProjectCommand()
                         {
                             ActionType = ActionTypes.OrganizationToDoCommentAdded,
                             ProjectId = project.Id,
                             OrganizationId = organizationId,
                             UserId = CurrentUser.Id,
                             ToDoId = todo.Id,
                             Text = comment.Text,
                             ProjectSubject = project.Subject,
                             Link = Url.ActionAbsolute("ToDos", "Organization", new { organizationId = organizationId, projectId = project.Id }),
                             ToDoSubject = todo.Subject,
                             ToDoLink = Url.ActionAbsolute("Comments", "Organization", new { projectId = project.Id, toDoId = todo.Id, organizationId = organizationId }),
                             IsPrivate = project.IsPrivate || todo.IsPrivate || comment.IsPrivate,
                             CommentId = comment.Id,
                             SendNotifications = sendNotifications,
                             UserFullName = CurrentUser.FullName,
                             UserLink = Url.ActionAbsolute("Details", "Account", new { userObjectId = CurrentUser.Id }),
                             OrganizationName = project.Organization.Name,
                             OrganizationLink = Url.ActionAbsolute("Details", "Organization", new { objectId = project.Organization.Id, name = project.Organization.Name.ToSeoUrl() })
                         });

            return GetCommentModelFromComment(comment, project, toDoId);
        }

        public CommentModel GetCommentModelFromComment(ToDoComment c, OrganizationProject project, string toDoId)
        {
            return new CommentModel()
                       {
                           OrganizationId = project.Organization.Id,
                           AuthorFullName = c.UserFullName,
                           AuthorObjectId = c.UserObjectId,
                           CommentDate = c.Date,
                           CommentText = c.Text.NewLineToHtml().ActivateLinks(),
                           ProjectId = project.Id,
                           Id = c.Id,
                           ToDoId = toDoId,
                           IsDeletable = CurrentUser.IsAuthenticated && c.UserObjectId == CurrentUser.Id,
                           Attachments = c.Attachments.Select(a => new UrlViewModel()
                           {
                               IconUrl = a.IconUrl,
                               Url = a.Url,
                               Title = a.Title
                           }).ToList()
                       };
        }

        public bool DeleteComment(string organizationId, string projectId, string toDoId, string commentId)
        {
            var project = GetProject(organizationId, projectId);
            var todo = GetToDo(project, toDoId);
            var comment = todo.Comments.Where(c => c.Id == commentId).Single();
            todo.Comments.Remove(comment);
            project.ModificationDate = DateTime.Now;
            UpdateOrganization(project.Organization);
            bus.Send(new CommentDeletedCommand()
            {
                CommentId = comment.Id,
                UserObjectId = CurrentUser.Id
            });
            return true;
        }

        public SettingsModel GetSettings(string organizationId, string projectId)
        {
            var project = GetProject(organizationId, projectId);
            return new SettingsModel()
                       {
                           Id = project.Id,
                           Description = project.StateDescription,
                           IsPrivate = project.IsPrivate,
                           State = ((int)project.State).ToString(),
                           Subject = project.Subject,
                           ProjectStates = GetStates(),
                           OrganizationId = organizationId,
                           OrganizationName = project.Organization.Name,
                           IsEditable = IsOrganizationEditable(organizationId)
                       };
        }

        public void SaveSettings(SettingsModel model)
        {
            var project = GetProject(model.OrganizationId, model.Id);

            project.IsPrivate = model.IsPrivate;
            using (var noSqlSession = noSqlSessionFactory())
            {
                var initialState = project.State;
                project.State =
                    (Data.Enums.OrganizationProjectStates)
                    Enum.Parse(typeof(Data.Enums.OrganizationProjectStates), model.State);
                project.StateDescription = model.Description.RemoveNewLines().Sanitize();
                project.ModificationDate = DateTime.Now;
                var sbj = model.Subject;
                project.Subject = sbj.GetSafeHtml();
                UpdateOrganization(project.Organization);

                if (initialState != project.State)
                {
                    bus.Send(new OrganizationProjectCommand()
                                 {
                                     ActionType = ActionTypes.OrganizationProjectStateChanged,
                                     OrganizationId = model.OrganizationId,
                                     UserId = CurrentUser.Id,
                                     Text = Globalization.Resources.Services.OrganizationProjectStates.ResourceManager.GetString(project.State.ToString()) + (!string.IsNullOrEmpty(project.StateDescription) ? ": " + project.StateDescription : string.Empty),
                                     ProjectSubject = project.Subject,
                                     Link = GetProjectUrl(model.OrganizationId, project.Id),
                                     ProjectId = model.Id,
                                     IsPrivate = project.IsPrivate
                                 });
                }
            }
        }

        public List<SelectListItem> GetUserOrganizations()
        {
            var result = new List<SelectListItem>();

            foreach (var uo in UserService.GetCurrentUserOrganizations().Where(o => o.IsMember && o.Permission != UserRoles.Basic))
            {
                if (string.IsNullOrEmpty(uo.OrganizationName))
                {
                    uo.OrganizationName = GetOrganizationName(uo.OrganizationId);
                }

                result.Add(new SelectListItem()
                               {
                                   Text = uo.OrganizationName,
                                   Value = uo.OrganizationId
                               });
            }

            if (result.Any())
            {
                result.Insert(0, new SelectListItem()
                                     {
                                         Text = CurrentUser.FullName,
                                         Value = string.Empty,
                                         Selected = true
                                     });
            }

            return result;
        }

        public string GetOrganizationName(string objectId)
        {
            if (string.IsNullOrEmpty(objectId))
            {
                return null;
            }

            using (var session = noSqlSessionFactory())
            {
                return session.GetAll<Organization>().Where(o => o.Id == objectId).Select(o => o.Name).Single();
            }
        }

        private IList<SelectListItem> GetStates()
        {
            var types = Enum.GetNames(typeof(Data.Enums.OrganizationProjectStates));

            return types.Select(t => new SelectListItem()
                                         {
                                             Text =
                                                 Globalization.Resources.Services.OrganizationProjectStates.
                                                 ResourceManager.GetString(t),
                                             Value =
                                                 ((int)Enum.Parse(typeof(Data.Enums.OrganizationProjectStates), t)).
                                                 ToString()
                                         }).ToList();
        }

        private OrganizationProject GetProject(string organizationId, string projectId)
        {
            var org = GetOrganization(organizationId);
            var proj = org.Projects.Where(p => p.Id == projectId).Single();
            proj.Organization = org;
            return proj;
        }

        private string GetProjectUrl(string organizationId, string projectId)
        {
            return Url.RouteUrl("OrganizationProject", new RouteValueDictionary
                                                           {
                                                               {"controller", "Organization"},
                                                               {"organizationId", organizationId},
                                                               {"projectId", projectId}
                                                           });
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

                var user =
                    session.Find<Data.MongoDB.User>(Query.EQ("_id", mid)).SetFields("FirstName", "LastName").
                        SingleOrDefault();
                if (user != null)
                {
                    return user.FullName;
                }

                return string.Empty;
            }
        }

        private ToDo GetToDo(OrganizationProject project, string todoId)
        {
            return project.ToDos.Where(t => t.Id == todoId).SingleOrDefault();
        }

        private ProjectsListModel GetProjectList(Organization organization, bool isEditable, ProjectListViews view)
        {
            var model = new ProjectsListModel();
            model.OrganizationId = organization.Id;
            model.IsEditable = isEditable;

            IQueryable<OrganizationProject> q = (from p in organization.Projects
                                                 where (!p.IsPrivate || isEditable)
                                                 orderby p.State, p.ModificationDate descending
                                                 select p).AsQueryable();

            if (view == ProjectListViews.Active)
            {
                q = q.Where(p => p.State == OrganizationProjectStates.New || p.State == OrganizationProjectStates.Planned);
            }
            else
            {
                q = q.Where(p => p.State == OrganizationProjectStates.Finished || p.State == OrganizationProjectStates.Cancelled);
            }

            model.List = q.Select(p => GetProjectModelFromOrganzationProject(p, isEditable, organization.Id)).ToList();

            return model;
        }

        private ProjectViewModel GetProjectModelFromOrganzationProject(OrganizationProject p, bool isEditable,
                                                                       string organizationId)
        {
            return new ProjectViewModel()
                       {
                           Name = p.Subject,
                           ObjectId = p.Id,
                           OrganizationId = organizationId,
                           State = p.State,
                           TasksCount =
                               p.ToDos.Where(t => (!t.IsPrivate || isEditable) && !t.FinishDate.HasValue).Count()
                       };
        }

        public bool InviteUsers(OrganizationViewModel model)
        {
            foreach (var user in model.UsersToInvite.Where(u => u.UserId.HasValue).Select(u => u.UserId).Distinct())
            {
                AddMember(model.ObjectId, user.Value);
            }

            if (!string.IsNullOrEmpty(model.CustomInvitationText))
            {
                using (var session = noSqlSessionFactory())
                {
                    var org = GetOrganization(model.ObjectId);
                    org.CustomInvitationText = model.CustomInvitationText.FilterHtmlToWhitelist();
                    UpdateOrganization(org);
                }
            }

            //foreach (var user in model.UsersToInvite.Where(u => !u.UserId.HasValue).Select(u => u.InvitedUser).Distinct())
            //{
            //    InviteUser(model.ObjectId, user);
            //}

            if (!string.IsNullOrEmpty(model.InvitedUserEmails))
            {
                model.InvitedUserEmails = model.InvitedUserEmails.Replace("\n\r", "\n");
                model.InvitedUserEmails = model.InvitedUserEmails.Replace("\r\n", "\n");
                model.InvitedUserEmails = model.InvitedUserEmails.Replace("\r", "\n");
                model.InvitedUserEmails = model.InvitedUserEmails.Replace(",", "\n");
                model.InvitedUserEmails = model.InvitedUserEmails.Replace(";", "\n");
                foreach (var user in model.InvitedUserEmails.Split('\n'))
                {
                    InviteUser(model.ObjectId, user);
                }
            }

            return true;
        }

        public bool InviteUser(string organizationId, string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                return false;
            }

            email = email.Trim();

            if (string.IsNullOrEmpty(email))
            {
                return false;
            }

            using (var session = userSessionFactory.CreateContext(true))
            {
                var existing = session.UserEmails.SingleOrDefault(us => us.Email == email);

                if (existing == null)
                {
                    var u =
                    session.UserInvitations.SingleOrDefault(
                        uo => uo.UserEmail == email && uo.OrganizationId == organizationId);

                    if (u == null)
                    {
                        u = new Data.EF.Users.UserInvitation()
                        {
                            OrganizationId = organizationId,
                            UserEmail = email
                        };
                        session.UserInvitations.Add(u);
                    }

                    u.Joined = false;
                    u.InvitedByUserId = CurrentUser.DbId;

                    bus.Send(new OrganizationInviteCommand()
                                 {
                                     OrganizationLink =
                                         Url.ActionAbsolute("Details", "Organization", new { objectId = organizationId }),
                                     OrganizationId = organizationId,
                                     UserFullName = CurrentUser.FullName,
                                     UserLink =
                                         Url.ActionAbsolute("Details", "Account", new { userObjectId = CurrentUser.Id }),
                                     Email = email,
                                     UserObjectId = CurrentUser.Id,
                                     UserId = CurrentUser.DbId
                                 });
                }
                else
                {
                    AddMember(organizationId, existing.User.Id);
                }
            }

            return true;
        }

        public bool DeleteInvitedUser(string organizationId, string email)
        {
            using (var session = userSessionFactory.CreateContext(true))
            {

                var u =
                    session.UserInvitations.SingleOrDefault(
                        uo => uo.UserEmail == email && uo.OrganizationId == organizationId);

                if (u != null)
                {
                    session.UserInvitations.Remove(u);
                    return true;
                }
            }

            return false;
        }

        public bool AddMember(string organizationId, int userId)
        {
            using (var session = actionSessionFactory.CreateContext(true))
            {
                var u =
                       session.UserInterestingOrganizations.SingleOrDefault(
                           uo => uo.UserId == userId && uo.OrganizationId == organizationId);
                if (u == null)
                {
                    u = new UserInterestingOrganization()
                    {
                        OrganizationId = organizationId,
                        UserId = userId
                    };
                    session.UserInterestingOrganizations.Add(u);
                }

                u.IsConfirmed = true;
                u.IsMember = true;
                u.InvitedByUserId = CurrentUser.DbId;
                u.VoteCount = 1;
            }

            bus.Send(new OrganizationMemberAddCommand()
            {
                ObjectId = organizationId,
                UserId = CurrentUser.Id,
                AddedUserId = userId
            });

            return true;
        }

        public bool RemoveMember(string organizationId, int memberId)
        {
            using (var session = actionSessionFactory.CreateContext(true))
            {
                var user =
                    session.UserInterestingOrganizations.SingleOrDefault(
                        uo => uo.UserId == memberId && uo.OrganizationId == organizationId);
                if (user == null)
                {
                    return false;
                }

                session.UserInterestingOrganizations.Remove(user);
            }

            return true;
        }

        public List<MemberModel> GetCommentUserEmails(ToDo todo)
        {
            return (from c in todo.Comments
                    from e in GetDbUserEmails(c.UserObjectId)
                    let u = GetDbUser(c.UserObjectId)
                    where e.IsEmailConfirmed && e.SendMail
                    select new MemberModel()
                    {
                        FullName = u.FirstName + " " + u.LastName,
                        ObjectId = u.ObjectId,
                        Email = e.Email
                    }).ToList();
        }

        public bool DeleteProject(string organizationId, string projectId)
        {
            if (!IsOrganizationEditable(organizationId))
            {
                return false;
            }

            var org = GetOrganization(organizationId);
            var proj = org.Projects.SingleOrDefault(p => p.Id == projectId);
            if (proj == null)
            {
                return false;
            }

            org.Projects.Remove(proj);
            UpdateOrganization(org);

            return true;
        }

        private bool GetIsEditable(string organizationId)
        {
            var uo = GetUserInterestingOrganization(organizationId);
            return GetIsEditable(uo);
        }

        private bool GetIsEditable(UserInterestingOrganization uo)
        {
            return uo != null && uo.IsMember && uo.IsConfirmed && uo.Permission == (int)UserRoles.Admin;
        }

        private bool GetIsContributable(string organizationId)
        {
            var uo = GetUserInterestingOrganization(organizationId);
            return GetIsContributable(uo);
        }

        private bool GetIsContributable(UserInterestingOrganization uo)
        {
            return uo != null && uo.IsMember && uo.IsConfirmed;
        }

        public List<ProjectViewModel> GetProjects(string organizationId, ProjectListViews view)
        {
            var org = GetOrganization(organizationId);
            var isEditable = GetIsContributable(organizationId);
            return GetProjectList(org, isEditable, view).List;
        }

        public OrganizationIndexModel GetIndexModel()
        {
            using (var session = noSqlSessionFactory())
            {
                var model = new OrganizationIndexModel();
                model.Items = (from o in session.GetAll<Data.MongoDB.Organization>()
                               select new OrganizationIndexItemModel()
                                          {
                                              Id = o.Id,
                                              Name = o.Name,
                                              Type =
                                                  Globalization.Resources.Services.OrganizationTypes.ResourceManager.
                                                  GetString(o.Type.ToString()),
                                              MembersCount = GetOrganizationSupportersCount(o.Id),
                                              HasPicture = o.ProfilePictureId != null,
                                              Info = GetInfo(o),
                                              Contacts = GetContacts(o)

                                          }).ToList().OrderByDescending(o => o.MembersCount).ToList();
                return model;
            }
        }

        public void DeleteOrganization(string objectId)
        {
            using (var session = noSqlSessionFactory())
            {
                session.Delete<Data.MongoDB.Organization>(o => o.Id == objectId);
            }

            using (var actionSession = actionSessionFactory.CreateContext(true))
            {
                actionSession.UserInterestingOrganizations.Delete(o => o.OrganizationId == objectId);
            }
        }

        public ToDoModel TakeToDo(string organizationId, string projectId, string toDoId)
        {

            var project = GetProject(organizationId, projectId);
            var todo = GetToDo(project, toDoId);
            todo.ResponsibleUserId = CurrentUser.Id;
            UpdateOrganization(project.Organization);

            bus.Send(new OrganizationProjectCommand()
            {
                ActionType = ActionTypes.OrganizationToDoTaken,
                OrganizationId = organizationId,
                UserId = CurrentUser.Id,
                ToDoId = todo.Id,
                Text = todo.Subject,
                ProjectSubject = project.Subject,
                Link = Url.ActionAbsolute("ToDos", "Organization", new { organizationId = organizationId, projectId = project.Id }),
                ProjectId = projectId,
                IsPrivate = project.IsPrivate || todo.IsPrivate,
                UserFullName = CurrentUser.FullName,
                UserLink = Url.ActionAbsolute("Details", "Account", new { userObjectId = CurrentUser.Id }),
                OrganizationName = project.Organization.Name,
                OrganizationLink = Url.ActionAbsolute("Details", "Organization", new { objectId = project.Organization.Id, name = project.Organization.Name.ToSeoUrl() })
            });

            return GetToDoModelFromToDo(todo, project, true);
        }

        public bool SetMemberVisibility(string organizationId, int userId, bool isPublic)
        {
            using (var session = actionSessionFactory.CreateContext(true))
            {
                var uo =
                    session.UserInterestingOrganizations.SingleOrDefault(
                        u => u.OrganizationId == organizationId && u.UserId == userId);
                if (uo == null)
                {
                    return false;
                }

                uo.IsPrivate = !isPublic;
                return true;
            }
        }

        public void UpdateAllPictures()
        {
            using (var session = noSqlSessionFactory())
            {
                foreach (var org in session.GetAll<Data.MongoDB.Organization>())
                {
                    if (string.IsNullOrEmpty(org.ProfilePictureId))
                    {
                        continue;
                    }

                    var picture = session.GetFile<Data.MongoDB.User>(org.ProfilePictureId);
                    if (picture == null)
                    {
                        continue;
                    }

                    var thumb = PictureProcessor.ResizeImageFile(picture.File, 50);
                    var thumbId = session.SaveFile("OrganizationLogoThumb_" + org.Id, thumb, MediaTypeNames.Image.Jpeg);
                    org.ProfilePictureThumbId = thumbId;
                    session.Update(org);
                }
            }
        }

        public FileViewModel GetPicture(string id)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                return noSqlSession.GetFile<Data.MongoDB.Organization>(id);
            }
        }
    }
}
