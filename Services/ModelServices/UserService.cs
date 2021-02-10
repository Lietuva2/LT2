using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Data;
using System.Data.Entity.SqlServer;
using System.Data.Entity.Validation;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Mime;
using System.Security;
using System.Text;
using System.Transactions;
using System.Web;
using System.Web.Mvc;
using Bus.Commands;
using Data.EF.Actions;
using Data.EF.Users;
using Data.EF.Voting;
using Data.Enums;
using Data.Infrastructure.Sessions;
using Data.MongoDB;
using Data.MongoDB.Interfaces;
using Data.ViewModels.Account;
using Data.ViewModels.Base;
using Data.ViewModels.Comments;
using Data.ViewModels.Sponsor;
using EntityFramework.Extensions;
using Framework;
using Framework.Drawing;
using Framework.Enums;
using Framework.Hashing;
using Framework.Infrastructure.Logging;
using Framework.Infrastructure.Storage;
using Framework.Infrastructure.ValueInjections;
using Framework.Lists;
using Framework.Mvc.Helpers;
using Framework.Mvc.Lists;
using Framework.Mvc.Strings;
using Framework.Strings;
using Globalization;
using Globalization.Resources.Services;
using LinkedIn.ServiceEntities;
using MongoDB.Driver.Builders;

using Omu.ValueInjecter;
using Services.Classes;
using Services.Enums;
using Services.Infrastructure;
using Services.Session;
using Services.VIISP;
using Education = Data.MongoDB.Education;
using Months = Globalization.Resources.Services.Months;
using User = Data.EF.Users.User;
using System.Data.Entity.Core;
using Framework.Bus;
using Framework.Infrastructure;
using UserAward = Data.EF.Users.UserAward;

namespace Services.ModelServices
{
    public class UserService : BaseCommentableService, IService
    {
        private IUsersContextFactory usersContextFactory;
        private Func<INoSqlSession> noSqlSessionFactory;
        private IVotingContextFactory votingSessionFactory;
        private ReportingService reportingService;
        private AddressService addressService;
        private SearchService searchService;
        private ShortLinkService shortLinkService;
        private IActionsContextFactory actionSessionFactory;

        private CategoryService categoryService;
        private ILogger logger;

        public NewsFeedService NewsFeedService
        {
            get { return ServiceLocator.Resolve<NewsFeedService>(); }
        }

        public OrganizationService OrganizationService
        {
            get { return ServiceLocator.Resolve<OrganizationService>(); }
        }

        private readonly IBus bus;

        public int PageSizeUserActivity
        {
            get { return 10; }
        }

        public UrlHelper Url
        {
            get { return new UrlHelper(((MvcHandler) HttpContext.Current.Handler).RequestContext); }
        }

        public UserService(
            IUsersContextFactory usersSessionFactory,
            Func<INoSqlSession> mongoDbSessionFactory,
            IVotingContextFactory votingSessionFactory,
            IActionsContextFactory actionSessionFactory,
            AddressService addressService,
            ReportingService reportingService,
            SearchService searchService,
            CategoryService categoryService,
            CommentService commentService,
            ShortLinkService shortLinkService,
            IBus bus,
            ILogger logger)
            : base(commentService)
        {
            this.usersContextFactory = usersSessionFactory;
            this.reportingService = reportingService;
            this.noSqlSessionFactory = mongoDbSessionFactory;
            this.votingSessionFactory = votingSessionFactory;
            this.actionSessionFactory = actionSessionFactory;
            this.addressService = addressService;
            this.searchService = searchService;
            this.categoryService = categoryService;
            this.shortLinkService = shortLinkService;
            this.bus = bus;
            this.logger = logger;
        }

        public void LogUserActivity(string userName, string activity, string ipAddress)
        {
            if (string.IsNullOrEmpty(userName))
            {
                userName = CurrentUser.FacebookId.HasValue ? CurrentUser.FacebookId.ToString() : CurrentUser.FullName;
            }

            reportingService.LogUserActivity(userName, activity, ipAddress, LogTypes.LogInOrOut);
        }

        public UserInfo Create(UserCreateModel model)
        {
            return CreateUser(model.FirstName, model.LastName, model.UserName, model.Password, null, model.Email, false,
                              model.SendMail);
        }

        public bool ValidateUserName(string username)
        {
            using (var userSession = usersContextFactory.CreateContext())
            {
                var query = userSession.Users.Where(u => u.UserName == username);
                if (CurrentUser.IsAuthenticated)
                {
                    query = query.Where(u => u.Id != CurrentUser.DbId);
                }

                return !query.Any();
            }
        }

        public bool ValidateEmail(string email)
        {
            using (var userSession = usersContextFactory.CreateContext(true))
            {
                var query = userSession.UserEmails.Where(u => u.Email == email);
                if (CurrentUser.IsAuthenticated)
                {
                    query = query.Where(u => u.User.Id != CurrentUser.DbId);
                }

                var userEmail = query.SingleOrDefault();
                if (userEmail == null)
                {
                    return true;
                }

                if (userEmail.IsEmailConfirmed || userEmail.User.UserEmails.Count() == 1)
                {
                    return false;
                }

                userSession.UserEmails.Remove(userEmail);
                return true;
            }
        }

        public UserInfo Login(string userName, string password)
        {
            var user = GetUserInfoByUserName(userName);

            if (user != null)
            {
                if (user.Password == password.ComputeHash())
                {
                    if (CurrentUser.IsUnique && !CurrentUser.IsViispConfirmed)
                    {
                        UniqueUser(CurrentUser.FirstName, CurrentUser.LastName, CurrentUser.PersonCode,
                                   CurrentUser.AuthenticationSource, user.DbId);
                    }

                    return user;
                }
            }

            return null;
        }

        public UserInfo GetUserInfoByUserName(string userName)
        {
            using (var userSession = usersContextFactory.CreateContext())
            {
                var model = userSession.Users.Where(u => u.UserName == userName.Trim())
                                       .Select(GetUserInfoFromUser()).SingleOrDefault();

                if (model != null)
                {
                    GetAdditionalUserInfo(model);
                }

                return model;
            }
        }

        //public void UpdateUserIfUnique(UserInfo model)
        //{
        //    if (CurrentUser.IsUnique && (CurrentUser.FirstName != model.FirstName || CurrentUser.LastName != model.LastName || CurrentUser.PersonCode != model.PersonCode))
        //    {
        //        UpdateUniqueUser(CurrentUser.FirstName, CurrentUser.LastName, CurrentUser.PersonCode, CurrentUser.AuthenticationSource);
        //    }
        //}

        public UserAccountViewModel GetUserAccount(MongoObjectId userObjectId, UserViews view)
        {
            Data.MongoDB.User user = GetUser(userObjectId);
            if (user == null)
            {
                throw new ObjectNotFoundException();
            }

            return GetUserAccount(user, view);
        }

        public bool GetIsVisible(Data.MongoDB.User user, UserVisibility visibility)
        {
            if (user.DbId == CurrentUser.DbId)
            {
                return true;
            }

            if (CurrentUser.Role == UserRoles.Admin)
            {
                return true;
            }

            if (visibility == UserVisibility.Public)
            {
                return true;
            }

            if (visibility == UserVisibility.Registered)
            {
                return CurrentUser.IsAuthenticated;
            }

            if (visibility == UserVisibility.Unique)
            {
                return CurrentUser.IsUnique;
            }

            if (visibility == UserVisibility.Connected)
            {
                if (CurrentUser.LikedByUsers == null)
                {
                    using (var session = actionSessionFactory.CreateContext())
                    {
                        CurrentUser.LikedByUsers = (from u in session.UserInterestingUsers
                                                    where u.InterestingUsersId == CurrentUser.DbId
                                                    select u.InterestedUsersId).ToList();
                    }
                }

                return CurrentUser.LikedByUsers.Contains(user.DbId);
            }

            return false;
        }

        private string GetPropertyIfVisible(Data.MongoDB.User user, string prop, UserVisibility visibility,
                                            bool isProfileVisible = true)
        {
            if (GetIsVisible(user, visibility) && isProfileVisible)
            {
                return prop;
            }

            return null;
        }

        public CommentsModel GetCommentsModel(string id)
        {
            return GetCommentsModel(GetUser(id));
        }

        private CommentsModel GetCommentsModel(Data.MongoDB.User user)
        {
            var model = new CommentsModel()
                {
                    Comments = commentService.GetCommentsMostSupported(user, 0),
                    EntryId = user.Id,
                    Type = EntryTypes.User
                };

            foreach (ForAgainst forAgainst in Enum.GetValues(typeof (ForAgainst)))
            {
                model.CommentCounts.Add(forAgainst, commentService.GetCommentsCount(user, forAgainst));
            }

            return model;
        }

        public void UpdateShortLinks()
        {
            using (var session = noSqlSessionFactory())
            {
                foreach (var user in session.GetAll<Data.MongoDB.User>())
                {
                    user.ShortLink = GetShortLink(user);
                    session.Update(user);
                }
            }
        }

        public UserAccountViewModel GetUserAccount(Data.MongoDB.User user, UserViews view)
        {
            var isProfileVisible = GetIsVisible(user, user.Settings.Visibility) &&
                                   (user.LastActivityDate.AddMonths(18) > DateTime.Now ||
                                    user.MemberSince.AddMonths(18) > DateTime.Now);
            var orgs = GetUserOrganizations(user.DbId);
            var account = new UserAccountViewModel
                {
                    FullName = isProfileVisible ? user.FullName : "Paskyra nepasiekiama",
                    UserObjectId = user.Id,
                    IsLiked = IsUserLikedByCurrentUser(user.DbId),
                    MemberSince =
                        GetPropertyIfVisible(user, user.MemberSince.ToShortDateString(),
                                             user.Settings.DetailsVisibility.MemberSince, isProfileVisible),
                    Categories =
                        isProfileVisible
                            ? GlobalizedSentences.GetNumberOfCategoriesString(
                                                     categoryService.GetUserCategoriesCount(user.DbId))
                            : null,
                    LikedUsers = isProfileVisible ? GetLikedUsers(user.DbId) : new List<SimpleLinkView>(),
                    IsCurrentUser = CurrentUser.IsAuthenticated && user.DbId == CurrentUser.DbId,
                    HasProfilePicture =
                        user.ProfilePictureId != null && isProfileVisible &&
                        GetIsVisible(user, user.Settings.DetailsVisibility.Photo),
                    View = view,
                    MemberOfOrganizations =
                        isProfileVisible
                            ? GetOrganizationLinks(orgs.Where(o => o.IsMember && (!o.IsPrivate || CurrentUser.IsUserInOrganization(o.OrganizationId))).Select(o => o.OrganizationId))
                            : new List<SimpleLinkView>(),
                    LikedOrganizations =
                        isProfileVisible
                            ? GetOrganizationLinks(orgs.Where(o => !o.IsMember).Select(o => o.OrganizationId))
                            : new List<SimpleLinkView>(),
                    IsProfileVisible = isProfileVisible,
                    IsActivityVisible = GetIsVisible(user, user.Settings.DetailsVisibility.Activity) && isProfileVisible,
                    IsReputationVisible =
                        GetIsVisible(user, user.Settings.DetailsVisibility.Reputation) && isProfileVisible,
                    ShortLink = user.ShortLink ?? string.Empty
                };
            if (view == UserViews.Info)
            {

                account.PersonalInfo = GetPersonalInfo(user);
                account.EducationAndWork = GetEducationAndWork(user);
                account.Interests = GetInterests(user);
                account.Contacts = GetContacts(user);
            }

            var emails = GetUserEmailModels(user.DbId, true);

            account.CanSendMessage = emails.Any(e => e.IsConfirmed) &&
                                     !string.IsNullOrEmpty(CurrentUser.Email) && !account.IsCurrentUser &&
                                     GetIsVisible(user, user.Settings.DetailsVisibility.Contact);

            if (view == UserViews.Activity)
            {
                if (!account.IsActivityVisible)
                {
                    throw new SecurityException("Activity is not visible to the user");
                }

                account.UserActivityList = NewsFeedService.GetUserActivityPage(user.Id, 0);
            }

            if (view == UserViews.Reputation)
            {
                if (!account.IsReputationVisible)
                {
                    throw new SecurityException("Reputation is not visible to the user");
                }

                account.UserActivityList = NewsFeedService.GetUserReputationPage(user.Id, 0);
            }

            if (view == UserViews.Settings)
            {
                account.Settings = GetUserSettings(user.Id);
            }

            if (view == UserViews.Comments)
            {
                account.Comments = GetCommentsModel(user);
            }


            using (var session = usersContextFactory.CreateContext())
            {
                if (account.IsActivityVisible)
                {
                    try
                    {
                        account.Points = session.UserCategories.Where(p => p.UserId == user.DbId).Sum(p => p.Points);
                    }
                    catch (Exception)
                    {
                        account.Points = 0;
                    }

                    account.Awards =
                        session.UserAwards.Where(a => a.UserId == user.DbId).Select(a => a.AwardId).ToList();

                    account.Status = session.Status.Where(s => s.PointsNeeded <= account.Points).OrderByDescending(
                        s => s.PointsNeeded).Select(s => s.Id).FirstOrDefault();
                    account.IsOnline = session.ChatClients.Any(c => c.UserId == user.DbId);
                }

                account.IsBlocked =
                    session.BlackLists.Any(b => b.UserId == CurrentUser.DbId && b.BlockedUserId == user.DbId);

                if (CurrentUser.IsAuthenticated)
                {
                    account.CanSendMessage = account.CanSendMessage && !IsUserBlocked(user.DbId, CurrentUser.DbId.Value);
                }

                var dbUser = session.Users.Single(u => u.Id == user.DbId);
                account.IsUnique = dbUser.PersonCode != null;

                if (CurrentUser.Role == UserRoles.Admin)
                {
                    account.RequireUniqueAuthentication = dbUser.RequireUniqueAuthentication;
                    account.IsPolitician = dbUser.IsPolitician;
                }

                if (CurrentUser.Id == account.UserObjectId)
                {
                    account.IsAmbasador = dbUser.IsAmbasador;
                }
            }

            string userObjectId = user.Id;
            using (var actionSession = actionSessionFactory.CreateContext())
            {
                if (isProfileVisible)
                {
                    account.UsersThatLikeMe =
                        GlobalizedSentences.GetSupportingUsersString((from u in actionSession.UserInterestingUsers
                                                                      where u.InterestingUsersId == user.DbId
                                                                      select u.InterestingUsersId).Count());
                    account.MyProjectCount =
                        GlobalizedSentences.GetNumberOfProjectsString(actionSession.Actions.Where(
                            a =>
                            a.UserObjectId == userObjectId && a.ActionTypeId == (int) ActionTypes.JoinedProject &&
                            !a.IsDeleted && (!a.IsPrivate || CurrentUser.OrganizationIds.Contains(a.OrganizationId)))
                                                                                   .Select(v => v.ObjectId)
                                                                                   .Distinct()
                                                                                   .Count());

                    account.InvolvedIdeasCount =
                        GlobalizedSentences.GetNumberOfIdeasString(actionSession.Actions.Where(
                            a =>
                            a.UserObjectId == userObjectId &&
                            (!a.IsPrivate || CurrentUser.OrganizationIds.Contains(a.OrganizationId)) &&
                            (a.ActionTypeId == (int) ActionTypes.IdeaCommented ||
                             a.ActionTypeId == (int) ActionTypes.IdeaVersionLiked) && !a.IsDeleted)
                                                                                .Select(i => i.ObjectId)
                                                                                .
                                                                                 Distinct().
                                                                                 Count());
                }
                if (account.IsReputationVisible)
                {
                    account.Reputation = GetUserReputation(userObjectId, CurrentUser.OrganizationIds);
                }
            }

            using (var session = votingSessionFactory.CreateContext())
            {
                if (isProfileVisible)
                {
                    account.CommentsCount = GlobalizedSentences.GetNumberOfTimesString(
                        (from a in session.IssueComments
                         join i in session.Issues on a.IssueId equals i.ObjectId
                         where
                             a.UserObjectId == userObjectId &&
                             (!i.IsPrivateToOrganization || CurrentUser.OrganizationIds.Contains(i.OrganizationId))
                         select a.IssueId).Distinct().Count());

                    account.VotesCount = GlobalizedSentences.GetNumberOfTimesString(session.Votes.Where(
                        a =>
                        a.UserObjectId == userObjectId &&
                        (!a.Issue.IsPrivateToOrganization ||
                         CurrentUser.OrganizationIds.Contains(a.Issue.OrganizationId)))
                                                                                           .Select(v => v.IssueId)
                                                                                           .Distinct()
                                                                                           .Count());

                    account.IdeasCount = GlobalizedSentences.GetNumberOfSolutionsString(session.IdeaVersions.Where(
                        a =>
                        a.UserObjectId == userObjectId && !a.Idea.IsImpersonal &&
                        (!a.Idea.IsPrivate || CurrentUser.OrganizationIds.Contains(a.Idea.OrganizationId)))
                                                                                           .Select(v => v.IdeaId)
                                                                                           .Distinct()
                                                                                           .Count());

                    account.IssuesCount = GlobalizedSentences.GetNumberOfIssuesString(session.IssueVersions.Where(
                        a =>
                        a.UserObjectId == userObjectId &&
                        (!a.Issue.IsPrivateToOrganization ||
                         CurrentUser.OrganizationIds.Contains(a.Issue.OrganizationId)))
                                                                                             .Select(v => v.IssueId)
                                                                                             .Distinct()
                                                                                             .Distinct()
                                                                                             .Count());

                    account.ProblemsCount = GlobalizedSentences.GetNumberOfProblemsString(session.Problems.Where(
                        a => a.UserObjectId == userObjectId).Distinct().Count());
                }
            }

            if (CurrentUser.IsAuthenticated && user.DbId != CurrentUser.DbId)
            {
                bus.Send(new RelatedUserCommand
                    {
                        ActionType = ActionTypes.UserProfileViewed,
                        UserId = CurrentUser.Id,
                        RelatedUserId = user.Id
                    });
            }

            return account;
        }

        private int GetUserReputation(string userObjectId, List<string> organizationIds = null)
        {
            if (organizationIds == null)
            {
                organizationIds = new List<string>();
            }

            using (var actionSession = actionSessionFactory.CreateContext())
            {
                return (from a in actionSession.Actions
                        where
                            a.LikedUserObjectId == userObjectId && !a.IsDeleted &&
                            (!a.IsPrivate || organizationIds.Contains(a.OrganizationId))
                        select a.ActionType.Reputation).Sum() ?? 0;
            }
        }

        public bool IsUserBlocked(int userId, int blockedUserId)
        {
            using (var session = usersContextFactory.CreateContext())
            {
                return session.BlackLists.Any(b => b.BlockedUserId == blockedUserId && b.UserId == userId);
            }

        }

        public ContactsViewModel GetContacts(MongoObjectId userObjectId)
        {
            var user = GetUser(userObjectId);
            return GetContacts(user);
        }

        public InterestsViewModel GetInterests(MongoObjectId userObjectId)
        {
            var user = GetUser(userObjectId);
            return GetInterests(user);
        }

        public EducationAndWorkViewModel GetEducationAndWork(MongoObjectId userObjectId)
        {
            var user = GetUser(userObjectId);
            return GetEducationAndWork(user);
        }

        public PersonalInfoViewModel GetPersonalInfo(MongoObjectId userObjectId)
        {
            var user = GetUser(userObjectId);
            return GetPersonalInfo(user);
        }

        public PersonalInfoViewModel SavePersonalInfo(PersonalInfoEditModel personalInfo)
        {
            var user = GetUser(personalInfo.UserObjectId);
            if (user.UserName != personalInfo.UserName)
            {
                reportingService.LogUserActivity(user.Id,
                                                 string.Format("Username changed. Old value: {0}, new value: {1}",
                                                               user.UserName, personalInfo.UserName), null,
                                                 LogTypes.UserNameChanged);
            }
            user.InjectFrom<UniversalInjection>(personalInfo);
            user.UserName = personalInfo.UserName;
            user.Citizenship = personalInfo.Citizenship;
            user.Nationality = personalInfo.Nationality;

            if (!CurrentUser.IsUnique)
            {
                user.FirstName = personalInfo.FirstName;
                user.LastName = personalInfo.LastName;
            }
            if (personalInfo.BirthYear.HasValue && personalInfo.BirthMonth.HasValue && personalInfo.BirthDay.HasValue)
            {
                user.BirthDate =
                    new DateTime(personalInfo.BirthYear.Value, personalInfo.BirthMonth.Value,
                                 personalInfo.BirthDay.Value);
            }
            else
            {
                user.BirthDate = null;
            }
            if (!string.IsNullOrEmpty(personalInfo.MaritalStatusName))
            {
                user.MaritalStatus = (MaritalStatus) Enum.Parse(typeof (MaritalStatus), personalInfo.MaritalStatusName);
            }

            if (!string.IsNullOrEmpty(personalInfo.EmploymentStatusName))
            {
                user.EmploymentStatus =
                    (EmploymentStatus) Enum.Parse(typeof (EmploymentStatus), personalInfo.EmploymentStatusName);
            }

            using (var transactionScope = new TransactionScope())
            {
                using (var userSession = usersContextFactory.CreateContext(true))
                {
                    var dbUser = userSession.Users.SingleOrDefault(u => u.Id == user.DbId);
                    if (dbUser != null)
                    {
                        dbUser.FirstName = user.FirstName;
                        dbUser.LastName = user.LastName;
                        dbUser.UserName = user.UserName;
                    }
                }

                UpdateUser(user);
                transactionScope.Complete();
            }

            bus.Send(new UserCommand
                {
                    ActionType = ActionTypes.UpdatedPersonalInfo,
                    UserId = user.Id
                });

            AwardFullProfile(user);

            CurrentUser.FirstName = user.FirstName;
            CurrentUser.LastName = user.LastName;

            return GetPersonalInfo(user);
        }

        public InterestsViewModel SaveInterests(InterestsEditModel interests)
        {
            var user = GetUser(interests.UserObjectId);
            user.InjectFrom<UniversalInjection>(interests);
            UpdateUser(user);

            bus.Send(new UserCommand
                {
                    ActionType = ActionTypes.UpdatedInterests,
                    UserId = user.Id
                });

            AwardFullProfile(user);

            return GetInterests(user);
        }

        public ContactsViewModel SaveContacts(ContactsEditModel model)
        {
            var user = GetUser(model.UserObjectId);
            model.Country = model.Country.FirstCharToUpper();
            model.City = model.City.FirstCharToUpper();
            model.Municipality = model.Municipality.FirstCharToUpper();

            model.OriginCountry = model.OriginCountry.FirstCharToUpper();
            model.OriginCity = model.OriginCity.FirstCharToUpper();
            model.OriginMunicipality = model.OriginMunicipality.FirstCharToUpper();

            user.InjectFrom<UniversalInjection>(model);

            if (!string.IsNullOrEmpty(model.Country))
            {
                addressService.SaveAddress(model.Country, model.Municipality, model.City);
            }

            if (!string.IsNullOrEmpty(model.OriginCountry))
            {
                addressService.SaveAddress(model.OriginCountry, model.OriginMunicipality, model.OriginCity);
            }

            var municipalityId = addressService.GetMunicipalityId(user.Municipality, model.Country);
            user.MunicipalityId = municipalityId;
            if (municipalityId.HasValue)
            {
                var cityId = addressService.GetCityId(user.City, municipalityId.Value);
                if (cityId.HasValue)
                {
                    user.CityId = cityId;
                }
            }

            var originMunicipalityId = addressService.GetMunicipalityId(user.OriginMunicipality, model.OriginCountry);
            user.OriginMunicipalityId = originMunicipalityId;

            if (originMunicipalityId.HasValue)
            {
                var cityId = addressService.GetCityId(user.OriginCity, originMunicipalityId.Value);
                if (cityId.HasValue)
                {
                    user.OriginCityId = cityId;
                }
            }

            model.WebSites.BindUrls(user.WebSites);

            user.PhoneNumbers.Clear();
            if (model.PhoneNumbers != null)
            {
                foreach (var item in model.PhoneNumbers)
                {
                    var itemToSave = new Data.MongoDB.PhoneNumber()
                        {
                            Phone = item.Number,
                            Type = item.Type
                        };

                    user.PhoneNumbers.Add(itemToSave);
                }
            }

            UpdateUser(user);

            using (var session = usersContextFactory.CreateContext(true))
            {
                var dbUser = session.Users.Single(u => u.Id == user.DbId);
                var postedEmails = model.Emails.Select(m => m.Email).ToList();
                var deletedEmailUsers = dbUser.UserEmails.Where(e => !postedEmails.Contains(e.Email)).ToList();
                foreach (var deletedUser in deletedEmailUsers)
                {
                    session.UserEmails.Remove(deletedUser);
                }

                foreach (var email in model.Emails)
                {
                    bool isNew = false;
                    var existingEmail = dbUser.UserEmails.SingleOrDefault(e => e.Email == email.Email);
                    if (existingEmail == null)
                    {
                        existingEmail = new UserEmail()
                            {
                                Email = email.Email
                            };
                        dbUser.UserEmails.Add(existingEmail);
                        isNew = true;
                    }

                    existingEmail.SendMail = email.SendMail;
                    existingEmail.IsPrivate = email.IsPrivate;

                    session.SaveChanges();

                    if (isNew)
                    {
                        bus.Send(new EmailChangedCommand()
                            {
                                UserDbId = user.DbId,
                                Email = email.Email
                            });
                    }
                }
            }

            CurrentUser.Municipalities = null;

            AwardFullProfile(user);

            bus.Send(new UserCommand
                {
                    ActionType = ActionTypes.UpdatedContacts,
                    UserId = user.Id
                });

            return GetContacts(user);
        }

        public void AwardFullProfile(Data.MongoDB.User user)
        {
            if (user.ProfilePictureId != null &&
                user.BirthDate.HasValue &&
                (int) user.EmploymentStatus != 0 &&
                !string.IsNullOrEmpty(user.Specialties) &&
                user.Educations.Any() &&
                user.Positions.Any() &&
                !string.IsNullOrEmpty(user.PoliticalViews) &&
                !string.IsNullOrEmpty(user.Groups) &&
                !string.IsNullOrEmpty(user.Interests) &&
                !string.IsNullOrEmpty(user.Summary) &&
                user.CityId.HasValue &&
                user.OriginCityId.HasValue &&
                user.WebSites.Any())
            {
                AwardUser(user.DbId, UserAwards.FullProfile);
            }
            else
            {
                TakeBackAward(user.DbId, UserAwards.FullProfile);
            }
        }

        public bool AwardUser(string userObjectid, UserAwards award)
        {
            return AwardUser(GetUser(userObjectid).DbId, award);
        }

        public bool TakeBackAward(string userObjectid, UserAwards award)
        {
            return TakeBackAward(GetUser(userObjectid).DbId, award);
        }

        public bool AwardUser(int userId, UserAwards award)
        {
            using (var session = usersContextFactory.CreateContext())
            {
                var dbaward =
                    session.UserAwards.SingleOrDefault(a => a.UserId == userId && a.AwardId == (short) award);
                if (dbaward == null)
                {
                    dbaward = new Data.EF.Users.UserAward()
                        {
                            UserId = userId,
                            AwardId = (short) award
                        };
                    session.UserAwards.Add(dbaward);
                    var userObjectId =
                        session.Users.Where(u => u.Id == userId).Select(u => u.ObjectId).SingleOrDefault();
                    session.SaveChanges();

                    bus.Send(new RelatedUserCommand()
                        {
                            ActionType = ActionTypes.UserAwarded,
                            MessageDate = DateTime.Now,
                            UserId = CurrentUser.IsAuthenticated ? CurrentUser.Id : userObjectId,
                            RelatedUserId = userObjectId,
                            Text =
                                Globalization.Resources.Services.UserAward.ResourceManager.GetString(
                                    award.ToString())
                        });
                    return true;
                }
            }

            return false;
        }

        public bool TakeBackAward(int userId, UserAwards award)
        {
            using (var session = usersContextFactory.CreateContext(true))
            {
                var num =
                    session.UserAwards.Delete(a => a.UserId == userId && a.AwardId == (short) award);

                return num > 0;
            }
        }

        public PersonalInfoEditModel GetPersonalInfoForEdit(PersonalInfoEditModel personalInfo)
        {
            var pInfo = GetPersonalInfoForEdit(personalInfo.UserObjectId);
            personalInfo.Years = pInfo.Years;
            personalInfo.Months = pInfo.Months;
            personalInfo.Days = pInfo.Days;
            personalInfo.EmploymentStatuses = pInfo.EmploymentStatuses;
            personalInfo.MaritalStatuses = pInfo.MaritalStatuses;
            return personalInfo;
        }

        public PersonalInfoEditModel GetPersonalInfoForEdit(MongoObjectId userObjectId)
        {
            var user = GetUser(userObjectId);
            return new PersonalInfoEditModel()
                {
                    BirthYear = user.BirthDate.HasValue ? user.BirthDate.Value.Year : (int?) null,
                    BirthMonth = user.BirthDate.HasValue ? user.BirthDate.Value.Month : (int?) null,
                    BirthDay = user.BirthDate.HasValue ? user.BirthDate.Value.Day : (int?) null,
                    Years = GetYearsToCurrent().ToSelectList(),
                    Months = GetMonths().ToSelectList(),
                    Days = GetDays().ToSelectList(),
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    UserName = user.UserName,
                    Citizenship = user.Citizenship,
                    Nationality = user.Nationality,
                    EmploymentStatusName = user.EmploymentStatus.ToString(),
                    MaritalStatusName = user.MaritalStatus.ToString(),
                    EmploymentStatuses = GetEmploymentStatuses().ToSelectList(),
                    MaritalStatuses = GetMaritalStatuses().ToSelectList(),
                    UserObjectId = user.Id
                };
        }

        public ContactsEditModel GetContactsForEdit(MongoObjectId userObjectId)
        {
            var user = GetUser(userObjectId);
            var model = new ContactsEditModel();
            model.InjectFrom(user);
            model.UserObjectId = user.Id;
            foreach (var item in user.WebSites.OrderBy(e => e.Type))
            {
                var e = new UrlEditModel("WebSites");
                e.InjectFrom<UniversalInjection>(item);
                model.WebSites.Add(e);
            }
            foreach (var item in user.PhoneNumbers.OrderBy(e => e.Type))
            {
                var e = new PhoneNumberEditModel("PhoneNumbers")
                    {
                        Number = item.Phone,
                        Type = item.Type,
                        Id = item.Id
                    };
                model.PhoneNumbers.Add(e);
            }

            using (var session = usersContextFactory.CreateContext())
            {
                model.Emails = session.UserEmails.Where(e => e.UserId == user.DbId).Select(e => new EmailModel()
                    {
                        Email = e.Email,
                        IsConfirmed = e.IsEmailConfirmed,
                        IsPrivate = e.IsPrivate,
                        SendMail = e.SendMail
                    }).ToList();
            }

            return model;
        }

        public void AssignEducationAndWorkSelectLists(EducationAndWorkEditModel model)
        {
            model.Years = GetEducationYears().ToSelectList();
            model.WorkYears = GetYearsToCurrent().ToSelectList();
            model.Months = GetMonths().ToSelectList();
        }

        public InterestsEditModel GetInterestsForEdit(MongoObjectId userObjectId)
        {
            var user = GetUser(userObjectId);
            var model = new InterestsEditModel();
            model.InjectFrom(user);
            model.UserObjectId = user.Id;
            return model;
        }

        public EducationAndWorkViewModel SaveEducationAndWork(EducationAndWorkEditModel editModel)
        {
            var user = GetUser(editModel.UserObjectId);
            //user.InjectFrom<UniversalInjection>(personalInfo);
            user.Summary = editModel.Summary;
            user.Specialties = editModel.Specialties;

            foreach (var item in editModel.Educations)
            {
                if (string.IsNullOrEmpty(item.SchoolName))
                {
                    if (item.IsNew)
                    {
                        continue;
                    }

                    item.IsDeleted = true;
                }

                var i = item;
                var itemToSave = item.IsNew ? new Education() : user.Educations.Where(e => e.Id == i.Id).Single();
                if (item.IsDeleted)
                {
                    if (!item.IsNew)
                    {
                        user.Educations.Remove(itemToSave);
                    }
                    continue;
                }

                itemToSave.InjectFrom<UniversalInjection>(item);
                if (item.IsNew)
                {
                    user.Educations.Add(itemToSave);
                }
            }

            foreach (var item in editModel.Positions)
            {
                if (string.IsNullOrEmpty(item.CompanyName))
                {
                    if (item.IsNew)
                    {
                        continue;
                    }

                    item.IsDeleted = true;
                }

                var i = item;
                var itemToSave = item.IsNew ? new WorkPosition() : user.Positions.Where(e => e.Id == i.Id).Single();

                if (item.IsDeleted)
                {
                    if (!item.IsNew)
                    {
                        user.Positions.Remove(itemToSave);
                    }
                    continue;
                }
                itemToSave.InjectFrom<UniversalInjection>(item);
                if (item.IsCurrent)
                {
                    itemToSave.EndYear = null;
                    itemToSave.EndMonth = null;
                }
                if (item.IsNew)
                {
                    user.Positions.Add(itemToSave);
                }
            }

            foreach (var item in editModel.MemberOfParties)
            {
                if (string.IsNullOrEmpty(item.PartyName))
                {
                    if (item.IsNew)
                    {
                        continue;
                    }

                    item.IsDeleted = true;
                }

                var i = item;
                var itemToSave = item.IsNew
                                     ? new PoliticalParty()
                                     : user.MemberOfParties.Where(e => e.Id == i.Id).Single();
                if (item.IsDeleted)
                {
                    if (!item.IsNew)
                    {
                        user.MemberOfParties.Remove(itemToSave);
                    }
                    continue;
                }
                itemToSave.InjectFrom<UniversalInjection>(item);
                if (item.IsCurrent)
                {
                    itemToSave.EndYear = null;
                    itemToSave.EndMonth = null;
                }

                if (item.IsNew)
                {
                    user.MemberOfParties.Add(itemToSave);
                }
            }

            UpdateUser(user);

            bus.Send(new UserCommand
                {
                    ActionType = ActionTypes.UpdatedEducationAndWork,
                    UserId = user.Id
                });

            AwardFullProfile(user);

            return GetEducationAndWork(user);
        }

        public EducationAndWorkEditModel GetEducationAndWorkForEdit(MongoObjectId userObjectId)
        {
            var user = GetUser(userObjectId);
            var model = new EducationAndWorkEditModel();

            model.Summary = user.Summary;
            model.Specialties = user.Specialties;
            model.UserObjectId = user.Id;

            foreach (var item in user.Educations.OrderByDescending(e => e.YearTo))
            {
                var e = new EducationEditModel();
                e.InjectFrom<UniversalInjection>(item);
                model.Educations.Add(e);
            }
            if (model.Educations.Count == 0)
            {
                model.Educations.Add(new EducationEditModel());
            }

            foreach (var item in user.Positions.OrderByDescending(e => e.StartYear).ThenByDescending(e => e.StartMonth))
            {
                var e = new PositionEditModel();
                e.InjectFrom<UniversalInjection>(item);
                model.Positions.Add(e);
            }
            if (model.Positions.Count == 0)
            {
                model.Positions.Add(new PositionEditModel());
            }

            foreach (
                var item in user.MemberOfParties.OrderByDescending(e => e.StartYear).ThenByDescending(e => e.StartMonth)
                )
            {
                var e = new MemberOfPartiesEditModel();
                e.InjectFrom<UniversalInjection>(item);
                model.MemberOfParties.Add(e);
            }
            if (model.MemberOfParties.Count == 0)
            {
                model.MemberOfParties.Add(new MemberOfPartiesEditModel());
            }

            AssignEducationAndWorkSelectLists(model);

            return model;
        }

        public IEnumerable<TextValue> GetWebSiteTypes()
        {
            var statuses = Enum.GetNames(typeof (Data.MongoDB.Website.Types));
            return statuses.Select(s => new TextValue()
                {
                    Text = WebSiteTypes.ResourceManager.GetString(s),
                    Value = s
                });
        }

        public IEnumerable<TextValue> GetPhoneNumberTypes()
        {
            var statuses = Enum.GetNames(typeof (Data.MongoDB.PhoneNumber.Types));
            return statuses.Select(s => new TextValue()
                {
                    Text = PhoneNumberTypes.ResourceManager.GetString(s),
                    Value = s
                });
        }

        public string GetProfilePictureId(MongoObjectId userObjectId)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                var user = noSqlSession.Find<Data.MongoDB.User>(Query.EQ("_id", userObjectId)).SetFields("ProfilePictureId").SingleOrDefault();
                if (user != null)
                {
                    return user.ProfilePictureId;
                }

                return null;
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

                var user = noSqlSession.Find<Data.MongoDB.User>(Query.EQ("_id", userObjectId)).SetFields("ProfilePictureThumbId").SingleOrDefault();
                if (user != null)
                {
                    return user.ProfilePictureThumbId;
                }

                return null;
            }
        }

        public FileViewModel GetProfilePicture(MongoObjectId userObjectId)
        {
            var imageId = GetUser(userObjectId).ProfilePictureId;
            using (var noSqlSession = noSqlSessionFactory())
            {
                return noSqlSession.GetFile<Data.MongoDB.User>(imageId);
            }
        }

        public FileViewModel GetProfilePictureThumb(MongoObjectId userObjectId)
        {
            var imageId = GetUser(userObjectId).ProfilePictureThumbId;
            if (imageId == null)
            {
                return null;
            }

            using (var noSqlSession = noSqlSessionFactory())
            {
                return noSqlSession.GetFile<Data.MongoDB.User>(imageId);
            }
        }

        public FileViewModel GetPicture(string id)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                return noSqlSession.GetFile<Data.MongoDB.User>(id);
            }
        }

        public void SaveProfilePicture(MongoObjectId userObjectId, byte[] file, string contentType,
                                       bool createAction = true, byte[] thumb = null, bool resize = true)
        {
            SaveProfilePicture(GetUser(userObjectId), file, contentType, createAction, thumb);
        }

        public void SaveProfilePicture(Data.MongoDB.User user, byte[] file, string contentType,
                                       bool createAction = true, byte[] thumb = null, bool resize = true)
        {
            byte[] resizedPicture = file;
            if (resize)
            {
                resizedPicture = PictureProcessor.ResizeImageFile(file, 200);
            }

            if (thumb == null)
            {
                thumb = PictureProcessor.ResizeImageFile(file, 50);
            }

            using (var noSqlSession = noSqlSessionFactory())
            {
                var id = noSqlSession.SaveFile("ProfilePicture_" + user.Id, resizedPicture, contentType);
                user.ProfilePictureId = id;
                user.ProfilePictureHistory.Add(id);
                var thumbId = noSqlSession.SaveFile("ProfilePictureThumb_" + user.Id, thumb, contentType);
                user.ProfilePictureThumbId = thumbId;
                user.ProfilePictureHistory.Add(thumbId);
                noSqlSession.Update(user);
            }

            if (createAction)
            {
                bus.Send(new UserCommand
                    {
                        ActionType = ActionTypes.ChangedProfilePicture,
                        UserId = user.Id
                    });
            }
        }

        private IEnumerable<TextValue> GetMaritalStatuses()
        {
            var statuses = Enum.GetNames(typeof (Data.MongoDB.MaritalStatus));
            return statuses.Select(s => new TextValue()
                {
                    Text = MaritalStatuses.ResourceManager.GetString(s),
                    Value = s
                });
        }

        private IEnumerable<TextValue> GetEmploymentStatuses()
        {
            var statuses = Enum.GetNames(typeof (Data.MongoDB.EmploymentStatus));
            return statuses.Select(s => new TextValue()
                {
                    Text = EmploymentStatuses.ResourceManager.GetString(s),
                    Value = s
                });
        }

        public IEnumerable<TextValue> GetYearsToCurrent()
        {
            var list = new List<TextValue>();
            for (int year = DateTime.Now.Year; year >= DateTime.Now.Year - 100; year--)
            {
                list.Add(new TextValue(year.ToString(), year.ToString()));
            }

            return list;
        }

        public IEnumerable<TextValue> GetEducationYears()
        {
            var list = new List<TextValue>();
            for (int year = DateTime.Now.Year + 6; year >= DateTime.Now.Year - 100; year--)
            {
                list.Add(new TextValue(year.ToString(), year.ToString()));
            }

            return list;
        }

        public IEnumerable<TextValue> GetMonths()
        {
            var statuses = Enum.GetNames(typeof (Enums.Months));
            return statuses.Select(s => new TextValue()
                {
                    Text = Months.ResourceManager.GetString(s),
                    Value = ((int) Enum.Parse(typeof (Enums.Months), s)).ToString()
                });
        }

        private IEnumerable<TextValue> GetDays()
        {
            var list = new List<TextValue>();
            for (int day = 1; day <= 31; day++)
            {
                list.Add(new TextValue(day.ToString(), day.ToString()));
            }

            return list;
        }

        public Data.MongoDB.User GetUser(MongoObjectId userObjectId)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                return noSqlSession.GetById<Data.MongoDB.User>(userObjectId);
            }
        }

        public Data.MongoDB.User GetUser(int userDbId)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                return noSqlSession.GetAll<Data.MongoDB.User>().SingleOrDefault(u => u.DbId == userDbId);
            }
        }

        protected override Data.MongoDB.Interfaces.ICommentable GetEntity(MongoObjectId id)
        {
            return GetUser(id);
        }

        private IQueryable<Data.MongoDB.User> GetUserQuery(MongoObjectId userObjectId)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                return noSqlSession.GetAll<Data.MongoDB.User>().Where(u => u.Id == userObjectId);
            }
        }

        private PersonalInfoViewModel GetPersonalInfo(Data.MongoDB.User user)
        {
            if (!GetIsVisible(user, user.Settings.Visibility))
            {
                return new PersonalInfoViewModel()
                    {
                        UserObjectId = user.Id,
                        IsCurrentUser = false
                    };
            }

            return new PersonalInfoViewModel()
                {
                    FullName = user.FullName,
                    BirthDate =
                        GetPropertyIfVisible(user,
                                             user.BirthDate.HasValue ? user.BirthDate.Value.ToShortDateString() : null,
                                             user.Settings.DetailsVisibility.BirthDate),
                    UserName = GetPropertyIfVisible(user, user.UserName, user.Settings.DetailsVisibility.UserName),
                    Citizenship =
                        GetPropertyIfVisible(user, user.Citizenship, user.Settings.DetailsVisibility.Citizenship),
                    Nationality =
                        GetPropertyIfVisible(user, user.Nationality, user.Settings.DetailsVisibility.Nationality),
                    EmploymentStatusName =
                        GetPropertyIfVisible(user,
                                             EmploymentStatuses.ResourceManager.GetString(
                                                 user.EmploymentStatus.ToString()),
                                             user.Settings.DetailsVisibility.EmploymentStatus),
                    MaritalStatusName =
                        GetPropertyIfVisible(user,
                                             MaritalStatuses.ResourceManager.GetString(user.MaritalStatus.ToString()),
                                             user.Settings.DetailsVisibility.MaritalStatus),
                    UserObjectId = user.Id,
                    IsCurrentUser = CurrentUser.IsAuthenticated && user.DbId == CurrentUser.DbId,
                };
        }

        private EducationAndWorkViewModel GetEducationAndWork(Data.MongoDB.User user)
        {
            if (!GetIsVisible(user, user.Settings.Visibility))
            {
                return new EducationAndWorkViewModel()
                    {
                        UserObjectId = user.Id,
                        IsCurrentUser = false,
                        Educations = new List<EducationViewModel>(),
                        Positions = new List<PositionVewModel>(),
                        MemberOfParties = new List<MemberOfPartiesViewModel>()
                    };
            }
            return new EducationAndWorkViewModel()
                {
                    Educations =
                        user.Educations != null && GetIsVisible(user, user.Settings.DetailsVisibility.Educations)
                            ? user.Educations.OrderByDescending(e => e.YearTo).Select(e => new EducationViewModel()
                                {
                                    Activities = e.Activities,
                                    Country = e.Country,
                                    Degree = e.Degree,
                                    FieldOfStudy = e.FieldOfStudy,
                                    Notes = e.Notes,
                                    SchoolName = e.SchoolName,
                                    YearFrom = e.YearFrom,
                                    YearTo = e.YearTo
                                }).ToList()
                            : new List<EducationViewModel>(),
                    Positions =
                        user.Positions != null && GetIsVisible(user, user.Settings.DetailsVisibility.Positions)
                            ? user.Positions.OrderByDescending(e => e.StartYear)
                                  .ThenByDescending(e => e.StartMonth)
                                  .Select(
                                      e => new PositionVewModel()
                                          {
                                              CompanyName = e.CompanyName,
                                              Description = e.Description,
                                              EndDate = GetEndDateString(e.EndYear, e.EndMonth),
                                              IsCurrent = e.IsCurrent,
                                              StartDate =
                                                  e.StartYear.HasValue && e.StartMonth.HasValue
                                                      ? new DateTime(e.StartYear.Value, e.StartMonth.Value, 1).ToString(
                                                          "y")
                                                      : e.StartYear.ToString() + CommonStrings.YearAbbr,
                                              Title = e.Title
                                          }).ToList()
                            : new List<PositionVewModel>(),
                    MemberOfParties =
                        user.MemberOfParties != null &&
                        GetIsVisible(user, user.Settings.DetailsVisibility.MemberOfParties)
                            ? user.MemberOfParties.OrderByDescending(e => e.StartYear)
                                  .ThenByDescending(e => e.StartMonth)
                                  .Select(
                                      e => new MemberOfPartiesViewModel()
                                          {
                                              Description = e.Description,
                                              EndDate = GetEndDateString(e.EndYear, e.EndMonth),
                                              IsCurrent = e.IsCurrent,
                                              PartyName = e.PartyName,
                                              PartyUrl = e.PartyUrl,
                                              StartDate =
                                                  e.StartYear.HasValue && e.StartMonth.HasValue
                                                      ? new DateTime(e.StartYear.Value, e.StartMonth.Value, 1).ToString(
                                                          "y")
                                                      : e.StartYear.ToString() + CommonStrings.YearAbbr,
                                          }).ToList
                                  ()
                            : new List<MemberOfPartiesViewModel>(),
                    UserObjectId = user.Id,
                    Summary =
                        GetPropertyIfVisible(user, user.Summary.NewLineToHtml(), user.Settings.DetailsVisibility.Summary),
                    Specialties =
                        GetPropertyIfVisible(user, user.Specialties.NewLineToHtml(),
                                             user.Settings.DetailsVisibility.Specialties),
                    IsCurrentUser = CurrentUser.IsAuthenticated && user.DbId == CurrentUser.DbId
                };
        }

        private string GetEndDateString(int? endYear, int? endMonth)
        {
            if (endYear.HasValue && endMonth.HasValue)
            {
                return new DateTime(endYear.Value, endMonth.Value, 1).ToString("y");
            }
            if (endYear.HasValue)
            {
                return endYear.ToString() + CommonStrings.YearAbbr;
            }

            return CommonStrings.ToPresent;
        }

        private InterestsViewModel GetInterests(Data.MongoDB.User user)
        {
            if (!GetIsVisible(user, user.Settings.Visibility))
            {
                return new InterestsViewModel()
                    {
                        UserObjectId = user.Id,
                        IsCurrentUser = false
                    };
            }
            var interests = new InterestsViewModel()
                {
                    Awards =
                        GetPropertyIfVisible(user, user.Awards.NewLineToHtml(), user.Settings.DetailsVisibility.Awards),
                    Interests =
                        GetPropertyIfVisible(user, user.Interests.NewLineToHtml(),
                                             user.Settings.DetailsVisibility.Interests),
                    PoliticalViews =
                        GetPropertyIfVisible(user, user.PoliticalViews.NewLineToHtml(),
                                             user.Settings.DetailsVisibility.PoliticalViews),
                    Groups =
                        GetPropertyIfVisible(user, user.Groups.NewLineToHtml(), user.Settings.DetailsVisibility.Groups),
                    UserObjectId = user.Id,
                    IsCurrentUser = CurrentUser.IsAuthenticated && user.DbId == CurrentUser.DbId
                };

            return interests;
        }

        private List<SimpleLinkView> GetLikedUsers(int dbId)
        {
            List<int> likedUserIds;
            using (var actionSession = actionSessionFactory.CreateContext())
            {
                likedUserIds = (from u in actionSession.UserInterestingUsers
                                where u.InterestedUsersId == dbId
                                select u.InterestingUsersId).ToList();

            }

            using (var userSession = usersContextFactory.CreateContext())
            {
                return (from u in userSession.Users
                        where likedUserIds.Contains(u.Id)
                        select new SimpleLinkView()
                            {
                                Name = u.FirstName + " " + u.LastName,
                                ObjectId = u.ObjectId
                            }).ToList();
            }
        }

        private ContactsViewModel GetContacts(Data.MongoDB.User user)
        {
            if (!GetIsVisible(user, user.Settings.Visibility))
            {
                return new ContactsViewModel()
                    {
                        UserObjectId = user.Id,
                        IsCurrentUser = false,
                        WebSites = new List<UrlViewModel>(),
                        PhoneNumbers = new List<PhoneNumberViewModel>()
                    };
            }

            var model = new ContactsViewModel();
            model.UserObjectId = user.Id;
            model.IsCurrentUser = CurrentUser.IsAuthenticated && user.DbId == CurrentUser.DbId;
            model.WebSites = GetIsVisible(user, user.Settings.DetailsVisibility.WebSites)
                                 ? user.WebSites.Select(w => new UrlViewModel
                                     {
                                         Title = w.Title,
                                         Url = w.Url
                                     }).ToList()
                                 : new List<UrlViewModel>();
            model.PhoneNumbers = GetIsVisible(user, user.Settings.DetailsVisibility.PhoneNumbers)
                                     ? user.PhoneNumbers.Select(w => new PhoneNumberViewModel
                                         {
                                             TypeName =
                                                 Globalization.Resources.Services.PhoneNumberTypes.ResourceManager
                                                              .GetString(w.Type.ToString()),
                                             Number = w.Phone
                                         }).ToList()
                                     : new List<PhoneNumberViewModel>();
            model.CurrentLocation = GetPropertyIfVisible(user, user.CurrentLocation,
                                                         user.Settings.DetailsVisibility.Address);
            model.OriginLocation = GetPropertyIfVisible(user, user.OriginLocation,
                                                        user.Settings.DetailsVisibility.OriginAddress);
            model.Emails = GetIsVisible(user, user.Settings.DetailsVisibility.Email)
                               ? GetUserEmailModels(user.DbId)
                               : new List<EmailModel>();
            return model;
        }

        private List<EmailModel> GetUserEmailModels(int userId, bool getAll = false)
        {
            using (var session = usersContextFactory.CreateContext())
            {
                var query = session.UserEmails.Where(e => e.UserId == userId);
                if (!getAll)
                {
                    query = query.Where(e => CurrentUser.DbId == userId || (!e.IsPrivate && e.IsEmailConfirmed));
                }

                return query.Select(e => new EmailModel()
                    {
                        Email = e.Email,
                        IsPrivate = e.IsPrivate,
                        SendMail = e.SendMail,
                        IsConfirmed = e.IsEmailConfirmed
                    }).ToList();
            }
        }

        private void UpdateUser(Data.MongoDB.User user)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                noSqlSession.Update(user);
            }
        }

        public void LikeUser(MongoObjectId userObjectId)
        {
            var user = GetUser(userObjectId);
            using (var actionSession = actionSessionFactory.CreateContext(true))
            {
                actionSession.LikeUser(CurrentUser.DbId.Value, user.DbId);
            }

            bus.Send(new RelatedUserCommand()
                {
                    ActionType = ActionTypes.LikedUser,
                    RelatedUserId = user.Id,
                    UserId = CurrentUser.Id
                });
        }

        public void UnlikeUser(string userObjectId)
        {
            var user = GetUser(userObjectId);
            using (var actionSession = actionSessionFactory.CreateContext(true))
            {
                actionSession.UserInterestingUsers.Delete(
                    a => a.InterestedUsersId == CurrentUser.DbId && a.InterestingUsersId == user.DbId);
                var actions = actionSession.Actions.Where(
                    a =>
                    a.ActionTypeId == (int) ActionTypes.LikedUser && a.RelatedUserObjectId == userObjectId &&
                    a.UserId == CurrentUser.DbId).ToList();
                foreach (var action in actions)
                {
                    action.IsDeleted = true;
                }
            }
        }

        private bool IsUserLikedByCurrentUser(int userDbId)
        {
            if (!CurrentUser.IsAuthenticated)
            {
                return false;
            }
            using (var actionSession = actionSessionFactory.CreateContext())
            {
                var q = from u in actionSession.UserInterestingUsers
                        where u.InterestedUsersId == CurrentUser.DbId && u.InterestingUsersId == userDbId
                        select u;

                return q.Any();
            }
        }

        public void ImportProfileInfo(Person linkedInUser, byte[] picture, bool overwrite)
        {
            var user = GetUser(CurrentUser.Id);
            if (linkedInUser.DateOfBirth != null)
            {
                try
                {
                    user.BirthDate = new DateTime(linkedInUser.DateOfBirth.Year, linkedInUser.DateOfBirth.Month,
                                                  linkedInUser.DateOfBirth.Day);
                }
                catch
                {
                }
            }

            if (overwrite)
            {
                user.Educations.Clear();
            }

            foreach (var edu in linkedInUser.Educations)
            {
                var linkedInEducation = edu;

                var education =
                    user.Educations.Where(
                        e =>
                        e.SchoolName == linkedInEducation.SchoolName &&
                        e.YearTo == linkedInEducation.EndDate.Year.ToString()).
                         SingleOrDefault();
                if (education == null)
                {
                    education = new Education();
                    user.Educations.Add(education);
                }

                education.SchoolName = linkedInEducation.SchoolName;
                education.Activities = linkedInEducation.Activities;
                education.Degree = linkedInEducation.Degree;
                education.FieldOfStudy = linkedInEducation.FieldOfStudy;
                education.YearFrom = linkedInEducation.StartDate != null
                                         ? linkedInEducation.StartDate.Year.ToString()
                                         : null;
                education.YearTo = linkedInEducation.EndDate != null ? linkedInEducation.EndDate.Year.ToString() : null;
                education.Notes = linkedInEducation.Notes;
            }

            if (overwrite)
            {
                user.Positions.Clear();
            }

            foreach (var pos in linkedInUser.Positions)
            {
                var linkedInPosition = pos;

                var position =
                    user.Positions.Where(
                        e =>
                        e.CompanyName == linkedInPosition.Company.Name && e.StartYear == linkedInPosition.StartDate.Year &&
                        (e.StartMonth == null && linkedInPosition.StartDate.Month == 0 ||
                         e.StartMonth == linkedInPosition.StartDate.Month))
                        .SingleOrDefault();
                if (position == null)
                {
                    position = new WorkPosition();
                    user.Positions.Add(position);
                }

                position.CompanyName = linkedInPosition.Company != null ? linkedInPosition.Company.Name : null;
                position.Description = linkedInPosition.Summary;
                position.Title = linkedInPosition.Title;
                position.IsCurrent = linkedInPosition.IsCurrent;
                position.StartYear = linkedInPosition.StartDate != null ? linkedInPosition.StartDate.Year : (int?) null;
                position.StartMonth = linkedInPosition.StartDate != null && linkedInPosition.StartDate.Month != 0
                                          ? linkedInPosition.StartDate.Month
                                          : (int?) null;
                position.EndYear = linkedInPosition.EndDate != null ? linkedInPosition.EndDate.Year : (int?) null;
                position.EndMonth = linkedInPosition.EndDate != null && linkedInPosition.EndDate.Month != 0
                                        ? linkedInPosition.EndDate.Month
                                        : (int?) null;
            }

            if (overwrite)
            {
                user.WebSites.Clear();
            }

            foreach (var url in linkedInUser.MemberUrls)
            {
                var linkedInMemberUrl = url;

                var website = user.WebSites.Where(e => e.Url == linkedInMemberUrl.Url).SingleOrDefault();
                if (website == null)
                {
                    website = new Website();
                    user.WebSites.Add(website);
                }

                website.Title = url.Name;
                website.Url = url.Url;
            }

            if (overwrite && !CurrentUser.IsUnique)
            {
                user.FirstName = linkedInUser.FirstName;
                user.LastName = linkedInUser.LastName;
            }

            if (!string.IsNullOrEmpty(linkedInUser.Summary) && (overwrite || string.IsNullOrEmpty(user.Summary)))
            {
                user.Summary = linkedInUser.Summary;
            }

            if (!string.IsNullOrEmpty(linkedInUser.Specialties) && (overwrite || string.IsNullOrEmpty(user.Specialties)))
            {
                user.Specialties = linkedInUser.Specialties;
            }

            if (!string.IsNullOrEmpty(linkedInUser.Honors) && (overwrite || string.IsNullOrEmpty(user.Awards)))
            {
                user.Awards = linkedInUser.Honors;
            }

            if (!string.IsNullOrEmpty(linkedInUser.MainAddress) && (overwrite || string.IsNullOrEmpty(user.Adress)))
            {
                user.Adress = linkedInUser.MainAddress;
            }

            if (!string.IsNullOrEmpty(linkedInUser.Associations) && (overwrite || string.IsNullOrEmpty(user.Groups)))
            {
                user.Groups = linkedInUser.Associations;
            }

            if (!string.IsNullOrEmpty(linkedInUser.Interests) && (overwrite || string.IsNullOrEmpty(user.Interests)))
            {
                user.Interests = linkedInUser.Interests;
            }

            if ((overwrite || user.ProfilePictureId == null) && picture != null)
            {
                SaveProfilePicture(user, picture, "image/jpeg", false, null, false);
            }

            UpdateUser(user);

            bus.Send(new UserCommand()
                {
                    ActionType = ActionTypes.ProfileImportedFromLinkedIn,
                    UserId = CurrentUser.Id
                });
        }

        public bool ResetPassword(PasswordResetModel model)
        {
            using (var userSession = usersContextFactory.CreateContext(true))
            {
                var user = (from u in userSession.UserEmails
                            where u.Email == model.Email
                            select u.User).SingleOrDefault();

                if (user != null)
                {
                    string password = new PasswordGenerator().GeneratePassword(8, true, true, false);
                    user.Password = password.ComputeHash();
                    user.RequireChangePassword = true;

                    bus.Send(new ChangePasswordCommand()
                        {
                            Email = model.Email,
                            Password = password,
                            UserName = user.UserName
                        });

                    reportingService.LogUserActivity(user.ObjectId, "Temp password sent to " + model.Email,
                                                     CurrentUser.Ip, LogTypes.PasswordReset);
                    return true;
                }
            }

            return false;
        }

        public SimpleListContainerModel GetUsersThatLikeMe(string userObjectId, int pageNumber)
        {
            var user = GetUser(userObjectId);
            List<int> ids;
            using (var actionSession = actionSessionFactory.CreateContext())
            {
                var query = from u in actionSession.UserInterestingUsers
                            where u.InterestingUsersId == user.DbId
                            select u;
                ids = query.Select(
                    i => i.InterestedUsersId).Distinct().OrderBy(q => q)
                           .GetExpandablePage(pageNumber, CustomAppSettings.PageSizeList).ToList();
            }

            using (var userSession = usersContextFactory.CreateContext())
            {
                var query = from u in userSession.Users
                            where ids.Contains(u.Id)
                            select new SimpleListModel()
                                {
                                    Id = u.ObjectId,
                                    Subject = u.FirstName + " " + u.LastName
                                };
                var result = query.ToList();
                result.ForEach(r => r.Type = EntryTypes.User);
                var simpleList = new SimpleListContainerModel();
                simpleList.List = new ExpandableList<SimpleListModel>(result, CustomAppSettings.PageSizeList);
                return simpleList;
            }
        }

        public SimpleListContainerModel GetMyCategories(string userObjectId)
        {
            var userId = GetUserDbId(userObjectId);
            if (!userId.HasValue)
            {
                return new SimpleListContainerModel();
            }

            var myCategories =
                categoryService.GetUserCategoriesModel(userId.Value).Select(c => new SimpleListModel()
                    {
                        Subject = c.CategoryName
                    }).ToList();

            var simpleList = new SimpleListContainerModel();
            simpleList.List = new ExpandableList<SimpleListModel>(myCategories, 100);
            return simpleList;
        }

        public UserInfo GetOAuthUser(long? facebookId, string email)
        {
            using (var userSession = usersContextFactory.CreateContext())
            {
                UserInfo model = null;
                if (facebookId.HasValue)
                {
                    model = userSession.Users
                        .Where(u => u.FacebookId == facebookId).Select(GetUserInfoFromUser()).SingleOrDefault();
                }

                if (model == null)
                {
                    model = userSession.UserEmails
                    .Where(u => u.Email == email).Select(e => e.User)
                    .Select(GetUserInfoFromUser()).SingleOrDefault();
                }

                if (model != null)
                {
                    model.FacebookId = facebookId;
                    GetAdditionalUserInfo(model);

                    if (CurrentUser.IsUnique)
                    {
                        UniqueUser(CurrentUser.FirstName, CurrentUser.LastName, CurrentUser.PersonCode,
                                   CurrentUser.AuthenticationSource, model.DbId);
                    }
                }

                return model;
            }
        }

        private void GetAdditionalUserInfo(UserInfo model)
        {
            if (model.IsAuthenticated)
            {
                //model.ExpertCategoryIds = GetUserExpertCategoryIds(model.DbId.Value);
                model.SelectedCategoryIds = categoryService.GetMyCategoryIds(model, false);
                model.Organizations = GetUserOrganizations(model.DbId.Value);
                foreach (var org in model.Organizations.Where(m => m.IsMember))
                {
                    org.OrganizationName = OrganizationService.GetOrganizationName(org.OrganizationId);
                }

                var user = GetUser(model.Id);
                if (user != null)
                {
                    model.VotesArePublic = user.Settings.VotesArePublic;
                    model.SupportedIdeaText = user.Settings.SupportedIdeaText;
                    model.VotedText = user.Settings.VotedText;
                    model.IsMainIdeaVoted = GetIsMainIdeaVoted(model.Id);
                    model.Reputation = GetUserReputation(model.Id);
                    user.LastActivityDate = DateTime.Now;
                    UpdateUser(user);
                }
            }

            model.Ip = HttpContext.Current.Request.UserHostAddress;
        }

        private bool GetIsMainIdeaVoted(string userId)
        {
            using (var session = votingSessionFactory.CreateContext())
            {
                if (string.IsNullOrEmpty(CustomAppSettings.MainIdeaId))
                {
                    return true;
                }

                return
                    session.IdeaVotes.Any(
                        v => v.UserObjectId == userId && v.IdeaId == CustomAppSettings.MainIdeaId);
            }
        }

        public AdditionalUniqueInfoModel GetAdditionalVotingInfo(string personCode)
        {
            using (var userSession = usersContextFactory.CreateContext())
            {
                var uniqueUser =
                    userSession.UniqueUsers.SingleOrDefault(
                        u => u.PersonCode == personCode);
                if (uniqueUser != null)
                {
                    var model = addressService.GetAdditionalUniqueInfoModel(uniqueUser.CityId);
                    if (model == null)
                    {
                        model = new AdditionalUniqueInfoModel()
                        {
                            FirstName = CurrentUser.FirstName,
                            LastName = CurrentUser.LastName,
                            PersonCode = CurrentUser.PersonCode
                        };
                    }

                    model.AddressLine = uniqueUser.AddressLine;
                    model.DocumentNo = uniqueUser.DocumentNo;
                    model.VotesArePublic = CurrentUser.VotesArePublic;
                    return model;
                }
            }

            return new AdditionalUniqueInfoModel()
                {
                    FirstName = CurrentUser.FirstName,
                    LastName = CurrentUser.LastName,
                    PersonCode = CurrentUser.PersonCode
                };
        }

        //public bool IsMailSendable(bool isMailSent, bool isEditable, List<int> categoryIds)
        //{
        //    return !isMailSent && CurrentUser.IsAuthenticated &&
        //        (IsUserExpert(categoryIds) || CurrentUser.Role == UserRoles.Admin || (isEditable && IsUserActiveInCategories(categoryIds)));
        //}

        public bool IsMailSendable()
        {
            return CurrentUser.IsAuthenticated &&
                (CurrentUser.Role == UserRoles.Admin || IsUserActive() || IsUserReputable() || IsUserActiveAndReputable());
        }

        //public bool IsUserActiveInCategories(List<int> categoryIds)
        //{
        //    var points = CurrentUser.Points.Where(p => p.CategoryId.HasValue && categoryIds.Contains(p.CategoryId.Value));
        //    if (points.Any())
        //    {
        //        return points.Max(p => p.Points) > 100;
        //    }

        //    return false;
        //}

        public bool IsUserActive()
        {
            if (CurrentUser.Points == null)
            {
                CurrentUser.Points = GetUserPointsPerCategory(CurrentUser.Id);
            }

            return CurrentUser.Points.Sum(p => p.Points) > 1000;
        }

        public bool IsUserReputable()
        {
            return CurrentUser.Reputation > 1000;
        }

        public bool IsUserActiveAndReputable()
        {
            if (CurrentUser.Points == null)
            {
                CurrentUser.Points = GetUserPointsPerCategory(CurrentUser.Id);
            }

            return CurrentUser.Points.Sum(p => p.Points) > 200 && CurrentUser.Reputation > 200;
        }

        public bool IsUserExpert(List<int> categoryIds)
        {
            return CurrentUser.ExpertCategoryIds.Intersect(categoryIds).Any();
        }

        private Expression<Func<User, UserInfo>> GetUserInfoFromUser()
        {
            return user => new UserInfo()
                       {
                           DbId = user.Id,
                           FirstName = user.FirstName,
                           LastName = user.LastName,
                           Id = user.ObjectId,
                           UserName = user.UserName,
                           Password = user.Password,
                           Email = user.UserEmails.Where(e => e.IsEmailConfirmed).Select(e => e.Email).FirstOrDefault(),
                           Role = (UserRoles)user.Role,
                           LanguageCode = user.Language.Code,
                           LanguageName = user.Language.Name,
                           IsAmbasador = user.IsAmbasador,
                           PostPermissionGranted = user.PostPermissionGranted ?? false,
                           HasSigned = user.Signed,
                           RequireChangePassword = user.RequireChangePassword,
                           FacebookId = user.FacebookId,
                           IsAuthenticated = true,
                           PersonCode = user.PersonCode,
                           AuthenticationSource = user.AuthSource,
                           RequireUniqueAuthentication = user.RequireUniqueAuthentication,
                           TutorialShown = user.TutorialShown,
                           FacebookPageLiked = user.PageLiked
                       };
        }

        public UserInfo CreateOAuthUser(OAuthLoginModel model)
        {
            var username = model.UserName;
            if (string.IsNullOrEmpty(username))
            {
                if (!string.IsNullOrEmpty(model.FacebookId))
                {
                    username = model.FacebookId;
                }
                else
                {
                    username = model.Email;
                }
            }

            int i = 1;
            while (!ValidateUserName(username))
            {
                username = model.UserName + i++;
            }

            model.UserName = username;
            var fbId = !string.IsNullOrEmpty(model.FacebookId) ? Convert.ToInt64(model.FacebookId) : (long?)null;

            return CreateUser(model.FirstName, model.LastName, username, null, fbId, model.Email, true);
        }

        public void SaveFacebookPicture(UserInfo userInfo)
        {
            if (!userInfo.FacebookId.HasValue)
            {
                return;
            }

            var user = GetUser(userInfo.Id);
            if (string.IsNullOrEmpty(user.ProfilePictureId))
            {
                SaveFacebookPicture(user.Id, userInfo.FacebookId.Value, true);
            }
        }

        private void SaveFacebookPicture(string userId, long facebookId, bool createAction = false)
        {
            var webClient = new WebClient();
            try
            {
                byte[] imageBytes =
                    webClient.DownloadData(string.Format("https://graph.facebook.com/{0}/picture?type=large", facebookId));
                byte[] thumb =
                    webClient.DownloadData(string.Format("https://graph.facebook.com/{0}/picture", facebookId));
                if (imageBytes != null)
                {
                    SaveProfilePicture(userId, imageBytes, MediaTypeNames.Image.Jpeg, createAction, thumb);
                }
            }
            catch(Exception ex)
            {
                logger.Error(ex);
            }
        }

        private UserInfo CreateUser(string firstName, string lastName, string userName, string password, long? facebookId, string email, bool isEmailConfirmed, bool sendMail = true)
        {
            if (CurrentUser.IsUnique && !CurrentUser.IsViispConfirmed)
            {
                firstName = CurrentUser.FirstName;
                lastName = CurrentUser.LastName;
            }

            if (string.IsNullOrEmpty(lastName) || string.IsNullOrEmpty(firstName))
            {
                return null;
            }

            var registeredOn = DateTime.Now;
            var mongoUser = new Data.MongoDB.User()
                                {
                                    FirstName = firstName.GetSafeHtml(),
                                    LastName = lastName.GetSafeHtml(),
                                    UserName = userName.GetSafeHtml(),
                                    MemberSince = registeredOn
                                };

            var user = new Data.EF.Users.User()
                           {
                               FirstName = firstName.GetSafeHtml(),
                               LastName = lastName.GetSafeHtml(),
                               UserName = userName.GetSafeHtml(),
                               FacebookId = facebookId,
                               ObjectId = mongoUser.Id.ToString(),
                               RegisteredOn = registeredOn
                           };
            user.UserEmails.Add(new UserEmail()
                {
                    Email = email,
                    IsEmailConfirmed = isEmailConfirmed,
                    SendMail = sendMail
                });

            if (!string.IsNullOrEmpty(password))
            {
                user.Password = password.ComputeHash();
            }

            List<UserInvitation> invitations;

            using (var userSession = usersContextFactory.CreateContext())
            {
                user.LanguageId = userSession.Languages.Where(l => l.Code == CurrentUser.LanguageCode).Select(l => l.Id).SingleOrDefault();
                if (user.LanguageId == 0)
                {
                    user.Language = userSession.Languages.First();
                }

                userSession.Users.Add(user);
                try
                {
                    userSession.SaveChanges();
                    bus.Send(new UserCreatedCommand()
                        {
                            UserId = user.ObjectId
                        });
                }
                catch (DbEntityValidationException ex)
                {
                    var msg = "User create failed: " + ex.Message;
                    foreach (var err in ex.EntityValidationErrors.Where(e => !e.IsValid))
                    {
                        foreach (var vErr in err.ValidationErrors)
                        {
                            msg += vErr.ErrorMessage + "; ";
                        }
                    }

                    throw new Exception(msg);
                }
                catch (Exception e)
                {
                    using (var mongoSession = noSqlSessionFactory())
                    {
                        mongoSession.Delete(mongoUser);
                    }
                    throw;
                }

                using (var mongoSession = noSqlSessionFactory())
                {
                    mongoUser.DbId = user.Id;
                    mongoUser.ShortLink = GetShortLink(mongoUser);
                    if (facebookId.HasValue)
                    {
                        mongoUser.WebSites.Add(new Website()
                        {
                            Title = "Facebook",
                            Url = @"https://www.facebook.com/" + facebookId,
                            Type = Website.Types.FaceBook
                        });
                    }

                    mongoSession.Add(mongoUser);

                    if (facebookId.HasValue)
                    {
                        SaveFacebookPicture(mongoUser.Id, facebookId.Value);
                        AddFacebookWebsite(mongoUser, facebookId.Value);
                    }
                }

                invitations = userSession.UserInvitations.Where(u => u.UserEmail == email).ToList();
                foreach (var inv in invitations)
                {
                    inv.Joined = true;
                }

                try
                {
                    userSession.SaveChanges();
                }
                catch (DbEntityValidationException ex)
                {
                    var msg = "User create failed with invitations: " + ex.Message;
                    foreach (var err in ex.EntityValidationErrors.Where(e => !e.IsValid))
                    {
                        foreach (var vErr in err.ValidationErrors)
                        {
                            msg += vErr.ErrorMessage + "; ";
                        }
                    }

                    throw new Exception(msg);
                }
            }

            if (invitations.Any())
            {
                if (CurrentUser.Organizations == null)
                {
                    CurrentUser.Organizations = new List<UserOrganization>();
                }
                using (var actionSession = actionSessionFactory.CreateContext(true))
                {
                    foreach (var inv in invitations)
                    {
                        if (!string.IsNullOrEmpty(inv.OrganizationId))
                        {
                            var ui = new Data.EF.Actions.UserInterestingOrganization()
                                         {
                                             UserId = user.Id,
                                             OrganizationId = inv.OrganizationId,
                                             IsConfirmed = true,
                                             IsMember = true,
                                             InvitedByUserId = inv.InvitedByUserId,
                                             VoteCount = 1
                                         };
                            actionSession.UserInterestingOrganizations.Add(ui);

                            CurrentUser.Organizations.Add(new UserOrganization()
                                {
                                    OrganizationId = inv.OrganizationId,
                                    IsMember = true,
                                    OrganizationName = GetOrganization(inv.OrganizationId).Name
                                });
                        }

                        if (!string.IsNullOrEmpty(inv.ProjectId))
                        {

                        }
                    }
                }
            }

            if (CurrentUser.IsUnique && !CurrentUser.IsViispConfirmed)
            {
                UniqueUser(firstName, lastName, CurrentUser.PersonCode, CurrentUser.AuthenticationSource, user.Id);
            }

            return new UserInfo
                       {
                           DbId = user.Id,
                           FirstName = user.FirstName,
                           LastName = user.LastName,
                           UserName = user.UserName,
                           Password = user.Password,
                           Id = user.ObjectId,
                           FacebookId = user.FacebookId,
                           Role = UserRoles.Basic,
                           LanguageCode = "lt-LT",
                           LanguageName = "Lietuvių"
                       };
        }

        private void AddFacebookWebsite(Data.MongoDB.User mongoUser, long facebookId)
        {
            mongoUser.WebSites.Add(new Website()
            {
                Title = "Facebook",
                Url = @"https://www.facebook.com/" + facebookId,
                Type = Website.Types.FaceBook
            });
        }

        private string GetShortLink(Data.MongoDB.User user)
        {
            return shortLinkService.GetShortLink(user.ShortLink ?? user.FullName,
                                                 GetDetailsUrl(user));
        }

        private string GetDetailsUrl(Data.MongoDB.User user)
        {
            return Url.Action("Details", "Account",
                                      new { userObjectId = user.Id, fullName = user.FullName.ToSeoUrl() });
        }

        public List<int> GetUserExpertCategoryIds(int userId)
        {
            using (var session = usersContextFactory.CreateContext())
            {
                return (from uc in session.UserCategories
                        where uc.UserId == userId && uc.IsExpert && uc.CategoryId.HasValue
                        select uc.CategoryId.Value).ToList();
            }
        }

        //public bool ConfirmFacebookUser(long fbId, string firstName, string lastName, string email)
        //{
        //    using (var userSession = usersSessionFactory())
        //    {
        //        var dbUser = userSession.GetSingle<User>(u => u.FacebookId == fbId);
        //        if (dbUser != null)
        //        {
        //            CurrentUser.FacebookId = 0;
        //            return false;
        //        }
        //    }

        //    var user = GetUser(CurrentUser.Id);
        //    user.FirstName = firstName;
        //    user.LastName = lastName;

        //    using (var transactionScope = new TransactionScope())
        //    {
        //        using (var userSession = usersSessionFactory())
        //        {
        //            var dbUser = userSession.GetSingle<User>(u => u.Id == user.DbId);
        //            if (dbUser != null)
        //            {
        //                dbUser.FirstName = firstName;
        //                dbUser.LastName = lastName;
        //                dbUser.FacebookId = fbId;
        //                dbUser.Email = email;
        //                dbUser.IsEmailConfirmed = true;
        //                userSession.Update(dbUser);
        //            }
        //        }

        //        UpdateUser(user);
        //        transactionScope.Complete();
        //    }

        //    CurrentUser.FacebookId = fbId;
        //    CurrentUser.FirstName = firstName;
        //    CurrentUser.LastName = lastName;

        //    return true;
        //}

        public bool FindUserByNameAndLastName(string firstName, string lastName)
        {
            using (var userSession = usersContextFactory.CreateContext())
            {
                var user =
                    userSession.Users.FirstOrDefault(u => u.FirstName == firstName && u.LastName == lastName);
                return user != null;
            }
        }

        public void DeleteUser(string userObjectId)
        {
            User user;
            using (var userSession = usersContextFactory.CreateContext())
            {
                user = userSession.Users.SingleOrDefault(u => u.ObjectId == userObjectId);
            }

            int userId;
            string personCode = null;
            if (user != null)
            {
                userId = user.Id;
                personCode = user.PersonCode;
            }
            else
            {
                using (var session = noSqlSessionFactory())
                {
                    var usr =
                        session.GetAll<Data.MongoDB.User>().Where(u => u.Id == userObjectId).SingleOrDefault();
                    if (usr != null)
                    {
                        userId = usr.DbId;
                    }
                    else
                    {
                        throw new Exception("User to delete was not found");
                    }
                }
            }

            using (var noSqlSession = noSqlSessionFactory())
            {
                if (noSqlSession.GetAll<Data.MongoDB.Idea>().Any(i => i.UserObjectId == userObjectId && !i.IsImpersonal))
                {
                    throw new Exception("Cannot delete: user has created ideas");
                }

                if (noSqlSession.GetAll<Data.MongoDB.Issue>().Any(i => i.UserObjectId == userObjectId))
                {
                    throw new Exception("Cannot delete: user has created issues");
                }
				
				if (noSqlSession.GetAll<Data.MongoDB.Problem>().Any(i => i.UserObjectId == userObjectId))
                {
                    throw new Exception("Cannot delete: user has created problems");
                }

                foreach (var idea in noSqlSession.GetAll<Data.MongoDB.Idea>())
                {
                    if (!idea.IsImpersonal && idea.SummaryWiki.Versions.Where(v => v.CreatorObjectId == userObjectId).Any())
                    {
                        throw new Exception("Cannot delete: user has created idea versions");
                    }
                }

                foreach (var issue in noSqlSession.GetAll<Data.MongoDB.Issue>())
                {
                    if (issue.SummaryWiki.Versions.Where(v => v.CreatorObjectId == userObjectId).Any())
                    {
                        throw new Exception("Cannot delete: user has created issue versions");
                    }
                }
            }

            using (var actionSession = actionSessionFactory.CreateContext(true))
            {
                foreach (var org in actionSession.UserInterestingOrganizations.Where(uo => uo.UserId == userId && uo.IsMember && uo.IsConfirmed))
                {
                    if (!actionSession.UserInterestingOrganizations.Any(u => u.OrganizationId == org.OrganizationId && u.UserId != userId && u.IsMember && u.IsConfirmed))
                    {
                        throw new Exception("Cannot delete: user is the only member of the organizationId=" +
                                            org.OrganizationId);
                    }
                }

                actionSession.Notifications.Delete(a => a.UserId == userId);
                actionSession.Notifications.Delete(a => a.Action.UserId == userId);

                actionSession.ActionCategories.Delete(a => a.Action.UserObjectId == userObjectId);

                actionSession.Actions.Delete(a => a.UserObjectId == userObjectId);

                foreach (var userOrg in actionSession.UserInterestingOrganizations.Where(uo => uo.UserId == userId))
                {
                    using (var noSqlSession = noSqlSessionFactory())
                    {
                        var org = noSqlSession.GetAll<Data.MongoDB.Organization>().Where(o => o.Id == userOrg.OrganizationId).SingleOrDefault();
                        foreach (var proj in org.Projects)
                        {
                            foreach (var todo in proj.ToDos)
                            {
                                if (todo.ResponsibleUserId == userObjectId)
                                {
                                    todo.ResponsibleUserId = null;
                                }
                            }
                        }

                        noSqlSession.Update(org);
                    }

                    actionSession.UserInterestingOrganizations.Remove(userOrg);
                }

                var userCats = actionSession.InterestingCategories.Where(uc => uc.UserId == userId);
                actionSession.InterestingCategories.Delete(userCats);
            }
            using (var userSession = usersContextFactory.CreateContext(true))
            {
                userSession.UserCategories.Delete(uc => uc.UserId == userId);
                userSession.UserAwards.Delete(uc => uc.UserId == userId);
                userSession.BankAccountItems.Delete(uc => uc.UserId == userId);
                userSession.WebToPayLogs.Delete(uc => uc.UserId == userId);
                userSession.ChatClients.Delete(c => c.UserId == userId);
                userSession.ChatGroupUsers.Delete(c => c.UserId == userId);
                userSession.ChatOpenDialogs.Delete(c => c.UserId == userId || c.OtherUserId == userId);
                userSession.ChatMessages.Delete(c => c.UserId == userId || c.OtherUserId == userId);
                var emails = userSession.UserEmails.Where(ue => ue.UserId == userId).Select(ue => ue.Email).ToList();
                if (emails.Any())
                {
                    userSession.UserEmails.Delete(e => e.UserId == userId);
                    userSession.UserInvitations.Delete(e => emails.Contains(e.UserEmail));
                }
                userSession.BlackLists.Delete(e => e.UserId == userId || e.BlockedUserId == userId);
                user = userSession.Users.SingleOrDefault(u => u.ObjectId == userObjectId);
                if (user != null)
                {
                    if (!string.IsNullOrEmpty(user.PersonCode))
                    {
                        userSession.UniqueUsers.Delete<Data.EF.Users.UniqueUser>(uu => uu.PersonCode == user.PersonCode);
                    }
                    userSession.Users.Remove(user);
                }
            }

            using (var noSqlSession = noSqlSessionFactory())
            {
                foreach (var idea in noSqlSession.GetAll<Data.MongoDB.Idea>())
                {
                    var version = idea.SummaryWiki.Versions.Where(v => v.SupportingUsers.Any(s => s.Id == userObjectId)).SingleOrDefault();
                    if (version != null)
                    {
                        version.SupportingUsers.Remove(version.SupportingUsers.Single(s => s.Id == userObjectId));
                        version.SupportingUserCount--;
                    }

                    idea.Comments.RemoveAll(c => c.UserObjectId == userObjectId);

                    var project =
                        noSqlSession.GetAll<Data.MongoDB.Project>().Where(p => p.IdeaId == idea.Id).SingleOrDefault();
                    if (project != null)
                    {
                        var pm = project.ProjectMembers.Where(p => p.UserObjectId == userObjectId).SingleOrDefault();
                        if (pm != null)
                        {
                            project.ProjectMembers.Remove(pm);
                        }

                        foreach (var todo in project.ToDos.Where(t => t.ResponsibleUserId == userObjectId))
                        {
                            todo.ResponsibleUserId = null;
                        }

                        foreach (var ms in project.MileStones.Where(m => m.ToDos.Any(t => t.ResponsibleUserId == userObjectId)))
                        {
                            foreach (var todo in ms.ToDos.Where(td => td.ResponsibleUserId == userObjectId))
                            {
                                todo.ResponsibleUserId = null;
                            }
                        }

                        noSqlSession.Update(project);
                    }

                    noSqlSession.Update(idea);
                }

                foreach (var issue in noSqlSession.GetAll<Data.MongoDB.Issue>())
                {
                    var count = issue.Comments.RemoveAll(c => c.UserObjectId == userObjectId);
                    if (count > 0)
                    {
                        noSqlSession.Update(issue);
                    }
                    
                }

                foreach (var problem in noSqlSession.GetAll<Data.MongoDB.Problem>())
                {
                    var count = problem.Comments.RemoveAll(c => c.UserObjectId == userObjectId);
                    if (count > 0)
                    {
                        noSqlSession.Update(problem);
                    }
                }

                using (var votingSession = votingSessionFactory.CreateContext(true))
                {
                    foreach (var vote in votingSession.Votes.Where(o => o.PersonCode == personCode))
                    {
                        var issue = noSqlSession.GetAll<Data.MongoDB.Issue>().SingleOrDefault(i => i.DbId == vote.IssueId);
                        if (Convert.ToInt32(vote.ForAgainst) == (int)ForAgainst.For)
                        {
                            issue.SupportingVotesCount--;
                        }
                        if (Convert.ToInt32(vote.ForAgainst) == (int)ForAgainst.Against)
                        {
                            issue.NonSupportingVotesCount--;
                        }

                        noSqlSession.Update(issue);

                        votingSession.Votes.Remove(vote);
                    }
                }

                var nouser = noSqlSession.GetById<Data.MongoDB.User>(userObjectId);
                noSqlSession.Delete(nouser);
            }

            searchService.DeleteIndex(userObjectId);
        }

        public bool MergeUsers(string userFromObjectId, string userToObjectId)
        {
            User userFrom, userTo;
            int userFromId, userToId;
            string userFromPersonCode = null, userToPersonCode = null;
            using (var userSession = usersContextFactory.CreateContext(true))
            {
                userFrom = userSession.Users.SingleOrDefault(u => u.ObjectId == userFromObjectId);
                userTo = userSession.Users.SingleOrDefault(u => u.ObjectId == userToObjectId);

                if (userFrom != null && !string.IsNullOrEmpty(userFrom.PersonCode))
                {
                    throw new Exception("Cannot merge from unique user");
                }

                if (userFrom != null && userTo != null)
                {
                    if (string.IsNullOrEmpty(userTo.Password))
                    {
                        userTo.Password = userFrom.Password;
                    }

                    if (string.IsNullOrEmpty(userTo.UserName))
                    {
                        userTo.UserName = userFrom.UserName;
                    }

                    if (string.IsNullOrEmpty(userTo.PersonCode))
                    {
                        userTo.PersonCode = userFrom.PersonCode;
                        userTo.AuthSource = userFrom.AuthSource;
                    }

                    var userToAwards = userTo.UserAwards;
                    foreach (var award in userFrom.UserAwards)
                    {
                        if (!userToAwards.Any(a => a.AwardId == award.AwardId))
                        {
                            userTo.UserAwards.Add(new UserAward()
                            {
                                AwardId = award.AwardId
                            });
                        }
                    }

                    var userToEmails = userTo.UserEmails.Select(e => e.Email).ToList();
                    foreach (var email in userFrom.UserEmails.ToList())
                    {
                        userSession.UserEmails.Remove(email);
                        if (!userToEmails.Contains(email.Email))
                        {
                            userTo.UserEmails.Add(new UserEmail()
                                {
                                    Email = email.Email,
                                    SendMail = email.SendMail,
                                    IsPrivate = email.IsPrivate,
                                    IsEmailConfirmed = email.IsEmailConfirmed,
                                    EmailConfirmationCode = email.EmailConfirmationCode
                                });
                        }
                    }

                    if (!userTo.FacebookId.HasValue)
                    {
                        userTo.FacebookId = userFrom.FacebookId;
                    }
                }
            }

            if (userFrom != null)
            {
                userFromId = userFrom.Id;
                userFromPersonCode = userFrom.PersonCode;
            }
            else
            {
                using (var session = noSqlSessionFactory())
                {
                    var user = session.GetAll<Data.MongoDB.User>().SingleOrDefault(u => u.Id == userFromObjectId);
                    if (user != null)
                    {
                        userFromId = user.DbId;
                    }
                    else
                    {
                        throw new Exception("UserFrom not found");
                    }
                }
            }
            string userToFullName = string.Empty;
            if (userTo != null)
            {
                userToId = userTo.Id;
                userToFullName = userTo.FirstName + " " + userTo.LastName;
                userToPersonCode = userTo.PersonCode;
            }
            else
            {
                using (var session = noSqlSessionFactory())
                {
                    var user =
                        session.GetAll<Data.MongoDB.User>().SingleOrDefault(u => u.Id == userToObjectId);
                    if (user != null)
                    {
                        userToId = user.DbId;
                        userToFullName = user.FullName;
                    }
                    else
                    {
                        throw new Exception("UserTo not found");
                    }
                }
            }


            using (var noSqlSession = noSqlSessionFactory())
            {
                foreach (var idea in noSqlSession.GetAll<Data.MongoDB.Idea>())
                {
                    if (idea.UserObjectId == userFromObjectId)
                    {
                        idea.UserObjectId = userToObjectId;
                        idea.UserFullName = userToFullName;
                    }

                    foreach (
                        var version in idea.SummaryWiki.Versions)
                    {
                        if (version.CreatorObjectId == userFromObjectId)
                        {
                            version.CreatorObjectId = userToObjectId;
                            version.CreatorFullName = userToFullName;
                        }
                        if (version.SupportingUsers.Any(u => u.Id == userFromObjectId))
                        {
                            if (version.SupportingUsers.Any(u => u.Id == userToObjectId))
                            {
                                version.SupportingUsers.Remove(version.SupportingUsers.Single(u => u.Id == userFromObjectId));
                                version.SupportingUserCount--;
                            }
                            else
                            {
                                version.SupportingUsers[version.SupportingUsers.IndexOf(version.SupportingUsers.Single(u => u.Id == userFromObjectId))].Id =
                                    userToObjectId;
                            }
                        }
                    }

                    foreach (var comment in idea.Comments.Where(c => c.UserObjectId == userFromObjectId))
                    {
                        comment.UserObjectId = userToObjectId;
                        comment.UserFullName = userToFullName;
                    }

                    foreach (var comment in idea.Comments)
                    {
                        foreach (var cComment in comment.Comments.Where(c => c.UserObjectId == userFromObjectId))
                        {
                            cComment.UserObjectId = userToObjectId;
                            cComment.UserFullName = userToFullName;
                        }
                    }

                    var project =
                        noSqlSession.GetAll<Data.MongoDB.Project>().Where(p => p.IdeaId == idea.Id).SingleOrDefault();
                    if (project != null)
                    {
                        var pm =
                            project.ProjectMembers.Where(p => p.UserObjectId == userFromObjectId).SingleOrDefault();
                        var pmTo =
                            project.ProjectMembers.Where(p => p.UserObjectId == userToObjectId).SingleOrDefault();
                        if (pm != null)
                        {
                            if (pmTo != null)
                            {
                                project.ProjectMembers.Remove(pm);
                            }
                            else
                            {
                                pm.UserObjectId = userToObjectId;
                            }
                        }

                        foreach (var todo in project.ToDos.Where(t => t.ResponsibleUserId == userFromObjectId))
                        {
                            todo.ResponsibleUserId = userToObjectId;
                        }

                        foreach (
                            var ms in
                                project.MileStones.Where(
                                    m => m.ToDos.Any(t => t.ResponsibleUserId == userFromObjectId))
                            )
                        {
                            foreach (var todo in ms.ToDos.Where(td => td.ResponsibleUserId == userFromObjectId))
                            {
                                todo.ResponsibleUserId = userToObjectId;
                            }
                        }

                        noSqlSession.Update(project);
                    }

                    noSqlSession.Update(idea);
                }

                foreach (var issue in noSqlSession.GetAll<Data.MongoDB.Issue>())
                {
                    if (issue.UserObjectId == userFromObjectId)
                    {
                        issue.UserObjectId = userToObjectId;
                        issue.UserFullName = userToFullName;
                    }

                    foreach (
                        var version in issue.SummaryWiki.Versions.Where(v => v.CreatorObjectId == userFromObjectId))
                    {
                        version.CreatorObjectId = userToObjectId;
                        version.CreatorFullName = userToFullName;
                    }

                    foreach (var comment in issue.Comments.Where(c => c.UserObjectId == userFromObjectId))
                    {
                        comment.UserObjectId = userToObjectId;
                        comment.UserFullName = userToFullName;
                    }

                    foreach (var comment in issue.Comments)
                    {
                        foreach (var cComment in comment.Comments.Where(c => c.UserObjectId == userFromObjectId))
                        {
                            cComment.UserObjectId = userToObjectId;
                            cComment.UserFullName = userToFullName;
                        }
                    }

                    using (var votingSession = votingSessionFactory.CreateContext(true))
                    {
                        var vote = votingSession.Votes.Where(
                            o => o.PersonCode == userFromPersonCode && o.IssueId == issue.DbId).SingleOrDefault();
                        if (vote != null)
                        {
                            var voteTo =
                                votingSession.Votes.Where(
                                    o => o.PersonCode == userToPersonCode && o.IssueId == issue.DbId).SingleOrDefault();

                            if (voteTo != null)
                            {
                                if (Convert.ToInt32(vote.ForAgainst) == (int)ForAgainst.For)
                                {
                                    issue.SupportingVotesCount--;
                                }
                                else
                                {
                                    issue.NonSupportingVotesCount--;
                                }

                                votingSession.Votes.Remove(vote);
                            }
                            else
                            {
                                vote.PersonCode = userToPersonCode;
                            }
                        }
                    }
                }
            }

            using (var actionSession = actionSessionFactory.CreateContext(true))
            {
                var fromOrganizations =
                    actionSession.UserInterestingOrganizations.Where(uo => uo.UserId == userFromId).ToList
                        ();
                var toOrganizations =
                    actionSession.UserInterestingOrganizations.Where(uo => uo.UserId == userToId).ToList();
                foreach (var org in fromOrganizations)
                {
                    var toOrg = toOrganizations.Where(o => o.OrganizationId == org.OrganizationId).SingleOrDefault();
                    if (toOrg != null)
                    {
                        toOrg.IsMember = org.IsMember || toOrg.IsMember;
                        toOrg.IsConfirmed = org.IsConfirmed || toOrg.IsConfirmed;
                        actionSession.UserInterestingOrganizations.Remove(org);
                    }
                    else
                    {
                        org.UserId = userToId;
                    }

                    using (var noSqlSession = noSqlSessionFactory())
                    {
                        var orgObj =
                            noSqlSession.GetAll<Data.MongoDB.Organization>().Where(o => o.Id == org.OrganizationId).
                                SingleOrDefault();
                        foreach (var proj in orgObj.Projects)
                        {
                            foreach (var todo in proj.ToDos)
                            {
                                if (todo.ResponsibleUserId == userFromObjectId)
                                {
                                    todo.ResponsibleUserId = userToObjectId;
                                }
                            }
                        }

                        noSqlSession.Update(org);
                    }
                }

                foreach (
                    var action in
                        actionSession.Actions.Where(a => a.UserObjectId == userFromObjectId)
                    )
                {
                    action.UserObjectId = userToObjectId;
                    action.UserFullName = userToFullName;
                    action.UserId = userToId;
                }

                foreach (
                    var action in
                        actionSession.Actions.Where(
                            a => a.RelatedUserObjectId == userFromObjectId))
                {
                    action.RelatedUserObjectId = userToObjectId;
                    action.RelatedUserFullName = userToFullName;
                }

                foreach (
                    var not in
                        actionSession.Notifications.Where(a => a.UserId == userFromId))
                {
                    var notTo =
                        actionSession.Notifications.Where(
                            a => a.ActionId == not.ActionId && a.UserId == userToId).
                            SingleOrDefault();
                    if (notTo != null)
                    {
                        actionSession.Notifications.Remove(not);
                    }
                    else
                    {
                        not.UserId = userToId;
                    }
                }

                var userCats =
                    actionSession.InterestingCategories.Where(uc => uc.UserId == userFromId);
                foreach (var cat in userCats)
                {
                    var userToCat =
                        actionSession.InterestingCategories.Where(
                            u => u.UserId == userToId && u.CategoryId == cat.CategoryId).SingleOrDefault();
                    if (userToCat != null)
                    {
                        actionSession.InterestingCategories.Remove(cat);
                    }
                    else
                    {
                        cat.UserId = userToId;
                    }
                }
            }

            using (var userSession = usersContextFactory.CreateContext(true))
            {
                var userCats = userSession.UserCategories.Where(uc => uc.UserId == userFromId);
                foreach (var cat in userCats)
                {
                    var userToCat =
                        userSession.UserCategories.Where(
                            u => u.UserId == userToId && u.CategoryId == cat.CategoryId).SingleOrDefault();
                    if (userToCat != null)
                    {
                        userToCat.Points += cat.Points;
                        userToCat.IsExpert = userToCat.IsExpert || cat.IsExpert;
                        userSession.UserCategories.Remove(cat);
                    }
                    else
                    {
                        cat.UserId = userToId;
                    }
                }
            }

            DeleteUser(userFromObjectId);

            return true;
        }

        public void SaveFacebookUserEmail(string email)
        {
            using (var session = usersContextFactory.CreateContext(true))
            {
                var user = session.Users.SingleOrDefault(u => u.Id == CurrentUser.DbId);
                if (user != null)
                {
                    var userEmail = user.UserEmails.SingleOrDefault(e => e.Email == email);
                    if (userEmail == null)
                    {
                        userEmail = new UserEmail()
                            {
                                Email = email
                            };

                        user.UserEmails.Add(userEmail);
                    }

                    userEmail.IsEmailConfirmed = true;
                }
            }
        }

        public string SaveFacebookId(long facebookId, bool replace)
        {
            using (var session = usersContextFactory.CreateContext(true))
            {
                var fbUser = session.Users.SingleOrDefault(u => u.FacebookId == facebookId && u.Id != CurrentUser.DbId);
                var user = session.Users.SingleOrDefault(u => u.Id == CurrentUser.DbId);

                if (user == null)
                {
                    return null;
                }

                if (fbUser == null)
                {
                    user.FacebookId = facebookId;
                    return null;
                }

                if (replace)
                {
                    fbUser.FacebookId = null;
                    user.FacebookId = facebookId;
                    return null;
                }

                return fbUser.FirstName + " " + fbUser.LastName;
            }
        }

        public bool ConfirmEmail(string code, string email)
        {
            using (var session = usersContextFactory.CreateContext(true))
            {
                var q = session.UserEmails.Where(c => c.EmailConfirmationCode == code);
                if (!string.IsNullOrEmpty(email))
                {
                    q = q.Where(c => c.Email == email);
                }
                var conf = q.SingleOrDefault();

                if (conf == null)
                {
                    return false;
                }

                conf.IsEmailConfirmed = true;
                conf.EmailConfirmationCode = null;
                CurrentUser.Email = email;
            }

            return true;
        }

        public bool SendEmailConfirmation()
        {
            using (var session = usersContextFactory.CreateContext())
            {
                var email = session.UserEmails.FirstOrDefault(u => u.UserId == CurrentUser.DbId);
                if (email != null)
                {
                    return SendEmailConfirmation(email.Email);
                }
            }

            return false;
        }

        public bool SendEmailConfirmation(string email)
        {
            bus.Send(new EmailChangedCommand()
            {
                Email = email
            });

            return true;
        }

        public bool SendMessage(string userObjectId, string message, string addressFrom, string subject)
        {
            var user = !string.IsNullOrEmpty(userObjectId) ? GetUser(userObjectId) : null;
            bus.Send(new SendPrivateMessageCommand()
            {
                UserDbId = user != null ? user.DbId : (int?)null,
                UserFromDbId = CurrentUser.IsAuthenticated ? CurrentUser.DbId : (int?)null,
                Message = message,
                AddressFrom = addressFrom,
                Subject = subject
            });

            return true;
        }

        public List<PointsPerCategoryModel> GetUserPointsPerCategory(string userObjectId)
        {
            var cats = categoryService.GetCategories();
            while (cats == null)
            {
                cats = categoryService.GetCategories();
            }

            using (var session = usersContextFactory.CreateContext())
            {
                var model = (from uc in session.UserCategories
                             where uc.User.ObjectId == userObjectId
                             orderby uc.Points descending
                             select new PointsPerCategoryModel()
                                        {
                                            CategoryId = uc.CategoryId,
                                            Points = uc.Points
                                        }).ToList();
                model.ForEach(p => p.CategoryName = cats.Where(c => c.ValueInt == p.CategoryId).Select(c => c.Text).SingleOrDefault() ?? CommonStrings.NoCategory);
                return model;
            }
        }

        public List<LanguageModel> GetLanguages()
        {
            using (var session = usersContextFactory.CreateContext())
            {
                return (from l in session.Languages
                        select new LanguageModel()
                                   {
                                       Code = l.Code,
                                       Name = l.Name,
                                       IsSelected = l.Code == CurrentUser.LanguageCode
                                   }).ToList();
            }
        }

        public LanguageModel ChangeUserLanguage(string code)
        {
            using (var session = usersContextFactory.CreateContext(true))
            {
                var lang = session.Languages.Where(l => l.Code == code).Single();
                if (CurrentUser.IsAuthenticated)
                {
                    var user =
                        session.Users.Where(l => l.Id == CurrentUser.DbId).Single();
                    user.LanguageId = lang.Id;
                }

                CurrentUser.LanguageCode = lang.Code;
                CurrentUser.LanguageName = lang.Name;



                return new LanguageModel()
                           {
                               Code = lang.Code,
                               Name = lang.Name
                           };
            }
        }

        private List<UserOrganization> GetUserOrganizations(int userId)
        {
            using (var session = actionSessionFactory.CreateContext())
            {
                return (from uo in session.UserInterestingOrganizations
                        where uo.UserId == userId
                        select new UserOrganization()
                                   {
                                       OrganizationId = uo.OrganizationId,
                                       IsMember = uo.IsConfirmed && uo.IsMember,
                                       Permission = (UserRoles)uo.Permission,
                                       IsPrivate = uo.IsPrivate
                                   }).ToList();
            }
        }

        public void ResetCurrentUserOrganizations()
        {
            CurrentUser.Organizations = null;
            GetCurrentUserOrganizations();
        }

        public List<UserOrganization> GetCurrentUserOrganizations()
        {
            if (CurrentUser.Organizations == null && CurrentUser.DbId.HasValue)
            {
                CurrentUser.Organizations = GetUserOrganizations(CurrentUser.DbId.Value);
            }

            if (CurrentUser.Organizations == null)
            {
                return new List<UserOrganization>();
            }

            return CurrentUser.Organizations;
        }

        public List<string> GetUserOrganizationIds(int? currentUserId)
        {
            if (!currentUserId.HasValue)
            {
                return new List<string>();
            }

            if (CurrentUser != null)
            {
                if (currentUserId == CurrentUser.DbId)
                {
                    if (CurrentUser.Organizations == null)
                    {
                        CurrentUser.Organizations = GetUserOrganizations(currentUserId.Value);
                    }

                    return CurrentUser.OrganizationIds;
                }
            }

            return GetUserOrganizations(currentUserId.Value).Select(o => o.OrganizationId).ToList();
        }

        private List<SimpleLinkView> GetOrganizationLinks(IEnumerable<string> organizationIds)
        {
            return (from id in organizationIds
                    let o = GetOrganization(id)
                    select new SimpleLinkView()
                               {
                                   Name = o.Name,
                                   ObjectId = o.Id
                               }).ToList();
        }

        private Data.MongoDB.Organization GetOrganization(string id)
        {
            using (var session = noSqlSessionFactory())
            {
                return session.GetById<Organization>(id);
            }
        }

        public IList<LabelValue> GetUsers(string prefix)
        {
            var firstName = prefix.Substring(0, prefix.LastIndexOf(' ') > 0 ? prefix.LastIndexOf(' ') : prefix.Length);
            string lastName = prefix.LastIndexOf(' ') > 0 && prefix.Length > prefix.LastIndexOf(' ') + 1 ? prefix.Substring(prefix.LastIndexOf(' ') + 1) : null;

            using (var session = usersContextFactory.CreateContext())
            {

                var query =
                    session.Users.Where(
                        u =>
                        (u.FirstName.StartsWith(firstName) && (lastName == null || u.LastName.StartsWith(lastName))) ||
                         (u.LastName.StartsWith(firstName) && (lastName == null || u.FirstName == lastName)));

                return (from q in query
                        select new LabelValue { label = q.FirstName + " " + q.LastName, value = q.FirstName + " " + q.LastName, IdInt = q.Id }).ToList();
            }
        }

        public bool ChangePassword(ChangePasswordModel model)
        {
            using (var session = usersContextFactory.CreateContext(true))
            {
                var user =
                    session.Users.Where(u => u.Id == CurrentUser.DbId).SingleOrDefault();
                if (user != null)
                {
                    if (user.Password != model.OldPassword.ComputeHash())
                    {
                        return false;
                    }

                    user.Password = model.NewPassword.ComputeHash();
                    user.RequireChangePassword = false;
                    return true;
                }
            }

            return false;
        }

        public List<SimpleListModel> GetUserFullNames(List<string> ids)
        {
            using (var session = usersContextFactory.CreateContext())
            {
                return (from u in session.Users
                        where ids.Contains(u.ObjectId)
                        select new SimpleListModel()
                                   {
                                       Id = u.ObjectId,
                                       Subject = u.FirstName + " " + u.LastName
                                   }).ToList();
            }
        }

        public string GetUserFullName(string id)
        {
            using (var session = usersContextFactory.CreateContext())
            {
                return (from u in session.Users
                        where u.ObjectId == id
                        select u.FirstName + " " + u.LastName).SingleOrDefault();
            }
        }

        public List<SimpleListModel> GetUserFullNamesByDbId(List<int> ids)
        {
            using (var session = usersContextFactory.CreateContext())
            {
                return (from u in session.Users
                        where ids.Contains(u.Id)
                        select new SimpleListModel()
                        {
                            Id = SqlFunctions.StringConvert((double)u.Id),
                            Subject = u.FirstName + " " + u.LastName
                        }).ToList();
            }
        }

        public void SignManifest()
        {
            using (var session = usersContextFactory.CreateContext(true))
            {
                var user = session.Users.SingleOrDefault(u => u.Id == CurrentUser.DbId);
                user.Signed = true;
                CurrentUser.HasSigned = true;
            }
        }

        public void BecomeAmbasador()
        {
            using (var session = usersContextFactory.CreateContext(true))
            {
                var user = session.Users.SingleOrDefault(u => u.Id == CurrentUser.DbId);
                user.IsAmbasador = true;
                AwardUser(CurrentUser.DbId.Value, UserAwards.Ambasador);
                CurrentUser.IsAmbasador = true;
            }
        }

        public void CancelAmbasador()
        {
            using (var session = usersContextFactory.CreateContext(true))
            {
                var user = session.Users.Single(u => u.Id == CurrentUser.DbId);
                user.IsAmbasador = false;
                TakeBackAward(CurrentUser.DbId.Value, UserAwards.Ambasador);
                CurrentUser.IsAmbasador = false;
            }
        }

        public void SetFacebookPermissionGranted(bool isGranted)
        {
            using (var session = usersContextFactory.CreateContext(true))
            {
                var user = session.Users.Single(u => u.Id == CurrentUser.DbId);
                user.PostPermissionGranted = isGranted;
                CurrentUser.PostPermissionGranted = isGranted;
            }
        }

        public void SetFacebookPageLiked(int? id, bool isLiked)
        {
            if (id.HasValue)
            {
                using (var session = usersContextFactory.CreateContext(true))
                {

                    var user = session.Users.Single(u => u.Id == id);
                    user.PageLiked = isLiked;
                }
            }

            CurrentUser.FacebookPageLiked = isLiked;
        }

        public BankLinkModel GetBankLinkModel(string ticket)
        {
            var model = new BankLinkModel();
            if (HttpContext.Current.IsDebuggingEnabled)
            {
                model.Target = "https://www.epaslaugos.lt/portal-test/external/services/authentication/v2/";
            }
            else
            {
                model.Target = "https://www.epaslaugos.lt/portal/external/services/authentication/v2/";
            }

            model.Values.Add("ticket", ticket);
            return model;
        }

        public BankLinkModel GetBankLinkModel(AuthenticationSources authenticationSource, bool isVoting)
        {
            var model = new BankLinkModel();
            switch (authenticationSource)
            {
                case AuthenticationSources.DnB:
                    var srcDnb = "302692874";
                    var timeDnb = DateTime.Now.ToString("yyyy.MM.dd hh:mm:ss");
                    model.Target = ConfigurationManager.AppSettings["AuthUrlDnb"] ?? "https://ib.dnb.lt/loginb2b.aspx";
                    model.Values.Add("SRC", srcDnb);
                    model.Values.Add("TIME", timeDnb);
                    model.Values.Add("TYPE", "EPS-01");
                    model.Values.Add("SIGNATURE", Convert.ToBase64String(DigitalSignature.Sign(srcDnb + timeDnb)));
                    return model;
                case AuthenticationSources.Sb:
                    var src = "302692874";
                    var time = DateTime.Now.ToString("yyyy.MM.dd hh:mm:ss");

                    model.Target = CustomAppSettings.SbAuthUrl;

                    model.Values.Add("SRC", src);
                    model.Values.Add("TIME", time);
                    model.Values.Add("TYPE", "LT2-01");
                    model.Values.Add("SIGNATURE", Convert.ToBase64String(DigitalSignature.Sign(src + time)));
                    return model;
                case AuthenticationSources.Rc:
                    var srcRc = "123456789";
                    var timeRc = DateTime.Now.ToString("yyyy.MM.dd hh:mm:ss");
                    model.Target = CustomAppSettings.RCAuthUrl;
                    model.Values.Add("SRC", srcRc);
                    model.Values.Add("TIME", timeRc);
                    model.Values.Add("TYPE", "EDS-02");
                    model.Values.Add("SIGNATURE", Convert.ToBase64String(DigitalSignature.Sign(srcRc + timeRc)));
                    return model;
                case AuthenticationSources.Ssc:
                    model.Target = "https://id.ssc.lt/lietuva2";
                    return model;
                case AuthenticationSources.IPasas:
                    var srcIpasas = "191630223";
                    var timeIpasas = DateTime.Now.ToString("yyyy.MM.dd hh:mm:ss");
                    model.Target = CustomAppSettings.IPasasAuthUrl;
                    model.Values.Add("SRC", srcIpasas);
                    model.Values.Add("TIME", timeIpasas);
                    model.Values.Add("TYPE", "EDS-01");
                    model.Values.Add("SIGNATURE", Convert.ToBase64String(DigitalSignature.Sign(srcIpasas + timeIpasas)));
                    return model;
                case AuthenticationSources.Nordea:
                    if (!HttpContext.Current.IsDebuggingEnabled)
                    {
                        return new BankLinkModel()
                            {
                                Contacts =
                                    new BankLinkModel.BankContacts()
                                        {
                                            Name = "Nordea",
                                            Email = "info@nordea.lt",
                                            PhoneNumber = "1554",
                                            CanConfirmByDonate = false
                                        }
                            };
                    }
                    model.Target = "https://netbank.nordea.com/pnbeidtest/eidn.jsp";
                    model.Values.Add("A01Y_ACTION_ID", "701");
                    model.Values.Add("A01Y_VERS", "0002");
                    model.Values.Add("A01Y_RCVID", "87654321LT");
                    model.Values.Add("A01Y_LANGCODE", "LT");
                    model.Values.Add("A01Y_STAMP", DateTime.Now.Ticks.ToString());
                    model.Values.Add("A01Y_IDTYPE", "02");
                    model.Values.Add("A01Y_RETLINK", Url.ActionAbsolute("BankLinkSuccess", "Account", new { bank = AuthenticationSources.Nordea }));
                    model.Values.Add("A01Y_CANLINK", Url.ActionAbsolute("BankLinkCancel", "Account", new { bank = AuthenticationSources.Nordea }));
                    model.Values.Add("A01Y_REJLINK", Url.ActionAbsolute("BankLinkReject", "Account", new { bank = AuthenticationSources.Nordea }));
                    model.Values.Add("A01Y_KEYVERS", "0001");
                    model.Values.Add("A01Y_ALG", "01");

                    var mac = string.Empty;
                    foreach (string val in model.Values.Keys)
                    {
                        mac += model.Values[val] + "&";
                    }

                    mac += "LEHTI" + "&";

                    model.Values.Add("A01Y_MAC", mac.GetMd5Hash().ToUpper());
                    return model;
                case AuthenticationSources.Citadele:
                    var srcCit = "302692874";
                    var timeCit = DateTime.Now.ToString("yyyy.MM.dd hh:mm:ss");
                    model.Target = ConfigurationManager.AppSettings["AuthUrlCitadele"] ?? "https://www.ibank.lt/cgi/ib01/ib100.pl?service=AUTHORIZE&system=LIETUVA";
                    model.Values.Add("SRC", srcCit);
                    model.Values.Add("TIME", timeCit);
                    model.Values.Add("TYPE", "PORT-01");
                    model.Values.Add("SIGNATURE", Convert.ToBase64String(DigitalSignature.Sign(srcCit + timeCit)));
                    return model;
                case AuthenticationSources.SwedBank:
                    return new BankLinkModel()
                        {
                            Contacts = new BankLinkModel.BankContacts()
                            {
                                Name = "Swedbank",
                                Email = "info@swedbank.lt",
                                PhoneNumber = "1884",
                                CanConfirmByDonate = !isVoting,
                                Donate = CurrentUser.IsAuthenticated && !isVoting ? new DonateModel()
                                {
                                    FirstName = CurrentUser.FirstName,
                                    LastName = CurrentUser.LastName,
                                    Email = CurrentUser.Email,
                                    PaymentType = "hanza",
                                    IsPersonCodeRequired = true
                                } : null
                            }
                        };
                case AuthenticationSources.Danske:
                    model.Target = ConfigurationManager.AppSettings["AuthUrlDanske"] ?? "https://ebankas.danskebank.lt/ib/site/authorization/login?system=LT20";
                    return model;
                case AuthenticationSources.Seb:
                    return new BankLinkModel()
                        {
                            Contacts = new BankLinkModel.BankContacts()
                            {
                                Name = "SEB",
                                Email = "info@seb.lt",
                                PhoneNumber = "1528",
                                CanConfirmByDonate = !isVoting,
                                Donate = CurrentUser.IsAuthenticated && !isVoting ? new DonateModel()
                                {
                                    FirstName = CurrentUser.FirstName,
                                    LastName = CurrentUser.LastName,
                                    Email = CurrentUser.Email,
                                    PaymentType = "vb2",
                                    IsPersonCodeRequired = true
                                } : null
                            }
                        };
                case AuthenticationSources.Medicinos:
                    return new BankLinkModel()
                    {
                        Contacts = new BankLinkModel.BankContacts()
                            {
                                Name = "Medicinos",
                                Email = "info@medbank.lt",
                                PhoneNumber = "(8 5) 264 4800",
                                CanConfirmByDonate = false
                            }
                    };
                default:
                    return null;
            }
        }

        public bool VerifyBankResponse(NameValueCollection getValues, NameValueCollection postValues, AuthenticationSources authenticationSource)
        {
            var activity = authenticationSource.ToString() + ": " + postValues.ToString() + " " + getValues.ToString();
            logger.Information(activity);
            reportingService.LogUserActivity(CurrentUser.Id, activity, CurrentUser.Ip, LogTypes.BankLink);

            switch (authenticationSource)
            {
                case AuthenticationSources.Nordea:
                    var mac = string.Empty;
                    foreach (string key in getValues.Keys)
                    {
                        if (key != "B02K_MAC" && key != "bank")
                        {
                            mac += getValues[key] + "&";
                        }
                    }

                    mac += "LEHTI" + "&";

                    return mac.GetMd5Hash().ToUpper() == getValues["B02K_MAC"];
                case AuthenticationSources.DnB:
                    try
                    {
                        if (DateTime.Now - Convert.ToDateTime(postValues["TIME"]) > TimeSpan.FromMinutes(1))
                        {
                            return false;
                        }

                        logger.Information(new[]
                            {
                                postValues["SRC"],
                                postValues["TIME"],
                                postValues["PERSON_CODE"],
                                postValues["PERSON_FNAME"],
                                postValues["PERSON_LNAME"],
                                postValues["COMPANY_CODE"],
                                postValues["COMPANY_NAME"]
                            }.Concatenate("; "));

                        return
                            DigitalSignature.VerifySignature(
                                HttpContext.Current.Server.MapPath("~/Certificates/Bank4598753.crt"),
                                Convert.FromBase64String(postValues["SIGNATURE"]), Encoding.GetEncoding("windows-1257"),
                                postValues["SRC"],
                                postValues["TIME"], postValues["PERSON_CODE"], postValues["PERSON_FNAME"],
                                postValues["PERSON_LNAME"],
                                postValues["COMPANY_CODE"], postValues["COMPANY_NAME"]);
                    }
                    catch (Exception ex)
                    {
                        var msg = ex.Message + ": " + postValues["SIGNATURE"] + "; " + postValues["SRC"] + "; " + postValues["TIME"] +
                                  "; " + postValues["PERSON_CODE"] + "; " + postValues["PERSON_FNAME"] + "; " +
                                  postValues["PERSON_LNAME"];
                        logger.Error(msg);
                        throw new Exception(msg);
                    }
                case AuthenticationSources.Sb:
                    try
                    {
                        if (DateTime.Now - Convert.ToDateTime(postValues["TIME"]) > TimeSpan.FromMinutes(1))
                        {
                            return false;
                        }
                        return
                            DigitalSignature.VerifySignature(
                                HttpContext.Current.Server.MapPath("~/Certificates/SBSTD_public.pem"),
                                Convert.FromBase64String(postValues["SIGNATURE"]), Encoding.GetEncoding("windows-1257"), postValues["SRC"],
                                postValues["TIME"], postValues["PERSON_CODE"], postValues["PERSON_FNAME"], postValues["PERSON_LNAME"]);
                    }
                    catch (Exception ex)
                    {
                        var msg = ex.Message + ": " + postValues["SIGNATURE"] + "; " + postValues["SRC"] + "; " + postValues["TIME"] +
                                  "; " + postValues["PERSON_CODE"] + "; " + postValues["PERSON_FNAME"] + "; " +
                                  postValues["PERSON_LNAME"];
                        logger.Error(msg);
                        throw new Exception(msg);
                    }
                case AuthenticationSources.Danske:
                    try
                    {
                        if (DateTime.Now - Convert.ToDateTime(postValues["TIME"]) > TimeSpan.FromMinutes(1))
                        {
                            return false;
                        }

                        logger.Information(new[]
                            {
                                postValues["SRC"],
                                postValues["TIME"],
                                postValues["PERSON_CODE"],
                                postValues["PERSON_FNAME"],
                                postValues["PERSON_LNAME"],
                                postValues["COMPANY_CODE"],
                                postValues["COMPANY_NAME"]
                            }.Concatenate("; "));
                        return
                            DigitalSignature.VerifySignature(
                                HttpContext.Current.Server.MapPath("~/Certificates/Danske.Click.crt"),
                                Convert.FromBase64String(postValues["SIGNATURE"]), Encoding.GetEncoding("windows-1257"),
                                postValues["SRC"],
                                postValues["TIME"], postValues["PERSON_CODE"], postValues["PERSON_FNAME"],
                                postValues["PERSON_LNAME"],
                                postValues["COMPANY_CODE"], postValues["COMPANY_NAME"]);
                    }
                    catch (Exception ex)
                    {
                        var msg = ex.Message + ": " + postValues["SIGNATURE"] + "; " + postValues["SRC"] + "; " + postValues["TIME"] +
                                  "; " + postValues["PERSON_CODE"] + "; " + postValues["PERSON_FNAME"] + "; " +
                                  postValues["PERSON_LNAME"];
                        logger.Error(msg);
                        throw new Exception(msg);
                    }
                case AuthenticationSources.Rc:
                    try
                    {
                        logger.Information("Post: " + postValues);
                        if (DateTime.Now - Convert.ToDateTime(postValues["TIME"]) > TimeSpan.FromMinutes(1))
                        {
                            return false;
                        }
                        return
                            DigitalSignature.VerifySignature(
                                HttpContext.Current.Server.MapPath("~/Certificates/id.rcsc.lt.certificate.crt"),
                                Convert.FromBase64String(postValues["SIGNATURE"]), Encoding.GetEncoding("windows-1257"), postValues["SRC"],
                                postValues["TIME"], postValues["PERSON_CODE"], postValues["PERSON_FNAME"], postValues["PERSON_LNAME"], postValues["SSO_TYPE"]);
                    }
                    catch (Exception ex)
                    {
                        var msg = ex.Message + ": " + postValues["SIGNATURE"] + "; " + postValues["SRC"] + "; " + postValues["TIME"] +
                                  "; " + postValues["PERSON_CODE"] + "; " + postValues["PERSON_FNAME"] + "; " +
                                  postValues["PERSON_LNAME"];
                        logger.Error(msg);
                        throw new Exception(msg);
                    }
                case AuthenticationSources.IPasas:
                    try
                    {
                        logger.Information("Post: " + postValues);
                        if (DateTime.Now - Convert.ToDateTime(postValues["TIME"]) > TimeSpan.FromMinutes(1))
                        {
                            return false;
                        }
                        return
                            DigitalSignature.VerifySignature(
                                HttpContext.Current.Server.MapPath("~/Certificates/ipasas_certificate.pem"),
                                Convert.FromBase64String(postValues["SIGNATURE"]), Encoding.GetEncoding("windows-1257"), postValues["SRC"],
                                postValues["TIME"], postValues["PERSON_CODE"], postValues["PERSON_FNAME"], postValues["PERSON_LNAME"]);
                    }
                    catch (Exception ex)
                    {
                        var msg = ex.Message + ": " + postValues["SIGNATURE"] + "; " + postValues["SRC"] + "; " + postValues["TIME"] +
                                  "; " + postValues["PERSON_FNAME"] + "; " +
                                  postValues["PERSON_LNAME"];
                        logger.Error(msg);
                        throw new Exception(msg);
                    }
                case AuthenticationSources.Ssc:
                    try
                    {
                        logger.Information("Post: " + postValues);
                        if (DateTime.Now - Convert.ToDateTime(postValues["TIME"]) > TimeSpan.FromMinutes(1))
                        {
                            return false;
                        }
                        return
                            DigitalSignature.VerifySignature(
                                HttpContext.Current.Server.MapPath("~/Certificates/ssc_certificate.pem"),
                                Convert.FromBase64String(postValues["SIGNATURE"]), Encoding.GetEncoding("windows-1257"), postValues["SRC"],
                                postValues["TIME"], postValues["PERSON_CODE"], postValues["PERSON_FNAME"], postValues["PERSON_LNAME"]);
                    }
                    catch (Exception ex)
                    {
                        var msg = ex.Message + ": " + postValues["SIGNATURE"] + "; " + postValues["SRC"] + "; " + postValues["TIME"] +
                                  "; " + postValues["PERSON_FNAME"] + "; " +
                                  postValues["PERSON_LNAME"];
                        logger.Error(msg);
                        throw new Exception(msg);
                    }
                case AuthenticationSources.Citadele:
                    try
                    {
                        if (DateTime.Now - Convert.ToDateTime(postValues["TIME"]) > TimeSpan.FromMinutes(1))
                        {
                            return false;
                        }
                        return
                            DigitalSignature.VerifySignature(
                                HttpContext.Current.Server.MapPath("~/Certificates/citadele.pem"),
                                Convert.FromBase64String(postValues["SIGNATURE"]), Encoding.GetEncoding("windows-1257"), postValues["SRC"],
                                postValues["TIME"], postValues["PERSON_CODE"], postValues["PERSON_FNAME"], postValues["PERSON_LNAME"]);
                    }
                    catch (Exception ex)
                    {
                        var msg = ex.Message + ": " + postValues["SIGNATURE"] + "; " + postValues["SRC"] + "; " + postValues["TIME"] +
                                  "; " + postValues["PERSON_CODE"] + "; " + postValues["PERSON_FNAME"] + "; " +
                                  postValues["PERSON_LNAME"];
                        logger.Error(msg);
                        throw new Exception(msg);
                    }
            }

            return false;
        }

        public string UniqueUser(NameValueCollection getValues, NameValueCollection postValues, AuthenticationSources authenticationSource)
        {
            switch (authenticationSource)
            {
                case AuthenticationSources.Nordea:
                    var names = SplitFullName(getValues["B02K_CUSTNAME"]);
                    return UniqueUser(names[0], names[1], getValues["B02K_CUSTID"], authenticationSource.ToString(), CurrentUser.DbId);
                    break;
                case AuthenticationSources.Sb:
                    return UniqueUser(postValues["PERSON_FNAME"], postValues["PERSON_LNAME"], postValues["PERSON_CODE"], authenticationSource.ToString(), CurrentUser.DbId);
                    break;
                case AuthenticationSources.DnB:
                    return UniqueUser(postValues["PERSON_FNAME"], postValues["PERSON_LNAME"], postValues["PERSON_CODE"], authenticationSource.ToString(), CurrentUser.DbId);
                    break;
                case AuthenticationSources.Danske:
                    return UniqueUser(postValues["PERSON_FNAME"], postValues["PERSON_LNAME"], postValues["PERSON_CODE"], authenticationSource.ToString(), CurrentUser.DbId);
                    break;
                case AuthenticationSources.Rc:
                    return UniqueUser(postValues["PERSON_FNAME"], postValues["PERSON_LNAME"], postValues["PERSON_CODE"], postValues["SSO_TYPE"] ?? authenticationSource.ToString(), CurrentUser.DbId);
                    break;
                case AuthenticationSources.IPasas:
                    return UniqueUser(postValues["PERSON_FNAME"], postValues["PERSON_LNAME"], postValues["PERSON_CODE"], authenticationSource.ToString(), CurrentUser.DbId);
                    break;
                case AuthenticationSources.Ssc:
                    return UniqueUser(postValues["PERSON_FNAME"], postValues["PERSON_LNAME"], postValues["PERSON_CODE"], authenticationSource.ToString(), CurrentUser.DbId);
                    break;
                case AuthenticationSources.Citadele:
                    return UniqueUser(postValues["PERSON_FNAME"], postValues["PERSON_LNAME"], postValues["PERSON_CODE"], authenticationSource.ToString(), CurrentUser.DbId);
                    break;
            }

            return null;
        }

        private string[] SplitFullName(string fullName)
        {
            string firstName, lastName;
            fullName = fullName.Trim();
            if (!fullName.Contains(" "))
            {
                firstName = fullName;
                lastName = fullName;
            }
            else
            {
                firstName = fullName.Substring(0, fullName.LastIndexOf(' '));
                lastName = fullName.Substring(fullName.LastIndexOf(' '));
            }

            return new[] { firstName, lastName };
        }

        public string UniqueUser(string firstName, string lastName, string personCode, string authSource, int? userDbId)
        {
            firstName = firstName.ToTitleCase().Trim();
            lastName = lastName.ToTitleCase().Trim();
            personCode = personCode.Trim();
            var user = UpdateUniqueUser(firstName, lastName, personCode, authSource, userDbId);

            //GetAdditionalUserInfo(CurrentUser);
            CurrentUser.IsConfirmedThisSession = true;

            if ((!CurrentUser.IsAuthenticated || CurrentUser.Id != user.ObjectId) && user != null)
            {
                return user.UserName;
            }

            CurrentUser.FirstName = firstName;
            CurrentUser.LastName = lastName;
            CurrentUser.PersonCode = personCode;
            CurrentUser.AuthenticationSource = authSource;

            return null;
        }

        private User UpdateUniqueUser(string firstName, string lastName, string personCode, string authSource, int? userDbId)
        {
            User user;
            using (var userSession = usersContextFactory.CreateContext(true))
            {
                user = userSession.Users.SingleOrDefault(u => u.PersonCode == personCode);
                if (user == null && userDbId.HasValue)
                {
                    user = userSession.Users.SingleOrDefault(u => u.Id == userDbId);
                }

                if (user != null)
                {
                    if (user.FirstName != firstName || user.LastName != lastName || user.PersonCode != personCode)
                    {
                        reportingService.LogUserActivity(user.ObjectId,
                            string.Format("Credentials updated. Old values: {0}:{1}:{2}; new values: {3}:{4}:{5}", user.FirstName, user.LastName, user.PersonCode, firstName, lastName, personCode),
                            CurrentUser.Ip, LogTypes.UserCredentialsUpdated);
                    }

                    user.FirstName = firstName;
                    user.LastName = lastName;
                    user.PersonCode = personCode;
                    user.AuthSource = authSource;

                    using (var session = noSqlSessionFactory())
                    {
                        var mongouser = session.GetAll<Data.MongoDB.User>().SingleOrDefault(u => u.Id == user.ObjectId);
                        if (mongouser != null)
                        {
                            mongouser.FirstName = firstName;
                            mongouser.LastName = lastName;
                            session.Update(mongouser);
                        }
                    }

                    AwardUser(user.Id, UserAwards.Unique);
                }
            }

            if (user != null)
            {
                searchService.UpdateIndex(user.ObjectId, EntryTypes.User);
            }

            return user;
        }

        public string Unsubscribe(string email, string sign)
        {
            if (Hashing.GetMd5Hash(email + "LT2rulez") != sign)
            {
                return null;
            }

            using (var session = usersContextFactory.CreateContext(true))
            {
                var user = session.UserEmails.Where(u => u.Email == email).SingleOrDefault();
                if (user != null)
                {
                    user.SendMail = false;
                    return user.User.ObjectId;
                }

                return null;
            }
        }

        public string GetUserObjectIdByPersonCode(string code)
        {
            using (var context = usersContextFactory.CreateContext())
            {
                return context.Users.Where(u => u.PersonCode == code).Select(u => u.ObjectId).SingleOrDefault();
            }
        }

        public string GetUserObjectId(int? dbId)
        {
            using (var context = usersContextFactory.CreateContext())
            {
                if (!dbId.HasValue)
                {
                    return null;
                }

                return context.Users.Where(u => u.Id == dbId).Select(u => u.ObjectId).SingleOrDefault();
            }
        }

        public string GetUserObjectId(string username)
        {
            using (var context = usersContextFactory.CreateContext())
            {
                if (string.IsNullOrEmpty(username))
                {
                    return null;
                }

                return context.Users.Where(u => u.UserName == username.Trim()).Select(u => u.ObjectId).SingleOrDefault();
            }
        }

        public int? GetUserDbId(string id)
        {
            using (var context = usersContextFactory.CreateContext())
            {
                if (string.IsNullOrEmpty(id))
                {
                    return null;
                }

                return context.Users.Where(u => u.ObjectId == id).Select(u => (int?)u.Id).SingleOrDefault();
            }
        }

        public List<SimpleListModel> GetUserObjectIds(string[] codes)
        {
            using (var context = usersContextFactory.CreateContext())
            {
                return context.Users.Where(u => codes.Contains(u.PersonCode)).Select(u => new SimpleListModel()
                                                                                              {
                                                                                                  Id = u.PersonCode,
                                                                                                  Subject = u.ObjectId
                                                                                              }).ToList();
            }
        }

        public string GetUniqueUserFullName(string code)
        {
            using (var context = usersContextFactory.CreateContext())
            {
                return context.UniqueUsers.Where(u => u.PersonCode == code).Select(u => u.FirstName + " " + u.LastName).SingleOrDefault();
            }
        }

        public List<SimpleListModel> GetUniqueUserFullNames(string[] codes)
        {
            using (var context = usersContextFactory.CreateContext())
            {
                return
                    context.UniqueUsers.Where(u => codes.Contains(u.PersonCode)).Select(
                        u => new SimpleListModel() { Id = u.PersonCode, Subject = u.FirstName + " " + u.LastName }).ToList();
            }
        }

        public bool IsEmailConfirmed()
        {
            using (var context = usersContextFactory.CreateContext())
            {
                var user =
                    context.UserEmails.Where(u => u.UserId == CurrentUser.DbId && u.IsEmailConfirmed).FirstOrDefault();
                if (user != null)
                {
                    if (string.IsNullOrEmpty(CurrentUser.Email))
                    {
                        CurrentUser.Email = user.Email;
                    }

                    return true;
                }
            }

            return false;
        }

        public void SaveAdditionalUniqueInfo(AdditionalUniqueInfoModel model)
        {
            CurrentUser.AdditionalInfo.AddressLine = model.AddressLine;
            CurrentUser.AdditionalInfo.DocumentNo = model.DocumentNo;
            CurrentUser.AdditionalInfo.CityId = addressService.SaveAddress(model.Country, model.Municipality, model.City);
            CurrentUser.AdditionalInfo.City = model.City;
            CurrentUser.AdditionalInfo.Country = model.Country;
            CurrentUser.AdditionalInfo.Municipality = model.Municipality;
            if (!CurrentUser.IsUnique)
            {
                CurrentUser.FirstName = model.FirstName;
                CurrentUser.LastName = model.LastName;
                CurrentUser.PersonCode = model.PersonCode;
            }

            CurrentUser.VotesArePublic = model.VotesArePublic;

            if (model.AllowSaveForNextUse)
            {
                using (var context = usersContextFactory.CreateContext(true))
                {
                    var uniqueUser = context.UniqueUsers.SingleOrDefault(u => u.PersonCode == CurrentUser.PersonCode);
                    if (uniqueUser == null)
                    {
                        uniqueUser = new UniqueUser()
                                         {
                                             PersonCode = CurrentUser.PersonCode
                                         };
                        context.UniqueUsers.Add(uniqueUser);
                    }

                    uniqueUser.FirstName = CurrentUser.FirstName;
                    uniqueUser.LastName = CurrentUser.LastName;
                    uniqueUser.AddressLine = CurrentUser.AdditionalInfo.AddressLine;
                    uniqueUser.DocumentNo = CurrentUser.AdditionalInfo.DocumentNo;
                    uniqueUser.CityId = CurrentUser.AdditionalInfo.CityId;
                }
            }

            if (CurrentUser.IsAuthenticated)
            {
                var user = GetUser(CurrentUser.Id);
                user.Municipality = model.Municipality;
                user.City = model.City;
                user.Country = model.Country;

                addressService.SaveAddress(model.Country, model.Municipality, model.City);

                user.MunicipalityId = addressService.GetMunicipalityId(model.Municipality, model.Country);

                if (user.MunicipalityId.HasValue)
                {
                    user.CityId = addressService.GetCityId(model.City, user.MunicipalityId.Value);
                }
                else
                {
                    user.CityId = addressService.GetCityId(model.City, model.Municipality, model.Country);
                }

                user.FirstName = CurrentUser.FirstName;
                user.LastName = CurrentUser.LastName;
                UpdateUser(user);

                using (var userSession = usersContextFactory.CreateContext(true))
                {
                    var dbUser = userSession.Users.Single(u => u.Id == CurrentUser.DbId);
                    dbUser.FirstName = CurrentUser.FirstName;
                    dbUser.LastName = CurrentUser.LastName;
                    dbUser.PersonCode = CurrentUser.PersonCode;
                }
            }
        }

        public SettingsModel GetUserSettings(string userObjectId)
        {
            var user = GetUser(userObjectId);
            return new SettingsModel()
                       {
                           Settings = user.Settings,
                           UserObjectId = userObjectId
                       };
        }

        public bool SaveSettings(SettingsModel model)
        {
            var user = GetUser(model.UserObjectId);
            user.Settings = model.Settings;
            UpdateUser(user);
            CurrentUser.VotesArePublic = model.Settings.VotesArePublic;
            CurrentUser.VotedText = model.Settings.VotedText;
            CurrentUser.SupportedIdeaText = model.Settings.SupportedIdeaText;
            return true;
        }

        protected override ActionTypes GetAddNewCommentActionType()
        {
            return ActionTypes.UserCommented;
        }

        protected override ActionTypes GetLikeCommentActionType()
        {
            return ActionTypes.UserCommentLiked;
        }

        protected override void SendCommentCommand(ICommentable entity, ActionTypes actionType, CommentView comment)
        {
            var relatedUserId = comment.AuthorObjectId;
            if (!string.IsNullOrEmpty(comment.ParentId) && actionType == ActionTypes.CommentCommented)
            {
                relatedUserId =
                    entity.Comments.Where(c => c.Id == comment.ParentId).Select(c => c.UserObjectId).SingleOrDefault();
            }
            bus.Send(new UserCommand
            {
                ActionType = actionType,
                ObjectId = comment.EntryId,
                Text = comment.CommentText,
                RelatedUserId = relatedUserId,
                UserId = CurrentUser.Id,
                CommentId = !string.IsNullOrEmpty(comment.ParentId) ? comment.ParentId : comment.Id,
                CommentCommentId = !string.IsNullOrEmpty(comment.ParentId) ? comment.Id : null,
                Link = Url.Action("Details", "Account", new { userObjectId = comment.EntryId, fullName = GetUserFullName(comment.EntryId).ToSeoUrl() })
            });
        }

        public void UpdateDbUser(string objectId)
        {
            var user = GetUser(objectId);

            using (var session = votingSessionFactory.CreateContext(true))
            {
                commentService.UpdateComments(objectId, user.Comments, EntryTypes.User);
            }
        }

        public void UpdateDb()
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                var users = noSqlSession.GetAll<Data.MongoDB.User>().ToList();
                foreach (var user in users)
                {
                    noSqlSession.Update(user);
                }
            }
        }

        public void RequireUniqueAuthentication(string userObjectId, bool require)
        {
            using (var session = usersContextFactory.CreateContext(true))
            {
                var user = session.Users.Single(u => u.ObjectId == userObjectId);
                user.RequireUniqueAuthentication = require;
            }
        }

        public void BlockUser(string userObjectId)
        {
            using (var session = usersContextFactory.CreateContext(true))
            {
                var user = GetUser(userObjectId);
                var block =
                    session.BlackLists.SingleOrDefault(b => b.UserId == CurrentUser.DbId && b.BlockedUserId == user.DbId);
                if (block != null)
                {
                    return;
                }

                block = new BlackList()
                    {
                        UserId = CurrentUser.DbId.Value,
                        BlockedUserId = user.DbId
                    };

                session.BlackLists.Add(block);
            }
        }

        public void UnblockUser(string userObjectId)
        {
            using (var session = usersContextFactory.CreateContext(true))
            {
                var user = GetUser(userObjectId);
                var block =
                    session.BlackLists.SingleOrDefault(b => b.UserId == CurrentUser.DbId && b.BlockedUserId == user.DbId);
                if (block == null)
                {
                    return;
                }

                session.BlackLists.Remove(block);
            }
        }

        public bool HasConfirmedEmails(ContactsEditModel model)
        {
            if (model.Emails == null)
            {
                model.Emails = new List<EmailModel>();
                return false;
            }

            using (var session = usersContextFactory.CreateContext())
            {
                var emails = model.Emails.Select(e => e.Email).ToList();
                return
                    session.UserEmails.Any(
                        u =>
                        u.User.ObjectId == model.UserObjectId && u.IsEmailConfirmed && emails.Contains(u.Email));
            }
        }

        public void ShowTutorial()
        {
            using (var session = usersContextFactory.CreateContext(true))
            {
                var user = session.Users.Single(u => u.Id == CurrentUser.DbId);
                user.TutorialShown = true;
                CurrentUser.TutorialShown = true;
            }
        }

        public bool ValidateMunicipality(string country, string municipality)
        {
            return addressService.ValidateMunicipality(country, municipality);
        }

        public void Logout()
        {
            using (var session = usersContextFactory.CreateContext(true))
            {
                session.ChatClients.Delete(c => c.UserId == CurrentUser.DbId);
            }
        }

        public List<string> GetSubscribedObjectIds(EntryTypes? type)
        {
            if (!CurrentUser.IsAuthenticated)
            {
                return new List<string>();
            }

            using (var session = actionSessionFactory.CreateContext())
            {
                int userId = CurrentUser.DbId.Value;
                var query = session.Subscribtions.Where(s => s.UserId == userId && s.Subscribed);
                if (type.HasValue)
                {
                    query = query.Where(q => q.EntryTypeId == (int)type);
                }

                return query.Select(s => s.ObjectId).ToList();
            }
        }

        public void UniqueUser(authenticationDataResponse data)
        {
            var personCode = data.authenticationAttribute.First().value;
            string firstName = null, lastName = null;
            AuthenticationSources source;
            foreach (var info in data.userInformation)
            {
                if (info.information == userInformation.firstName)
                {
                    firstName = info.value.Item.ToString();
                }
                if (info.information == userInformation.lastName)
                {
                    lastName = info.value.Item.ToString();
                }
            }

            firstName = firstName.ToTitleCase().Trim();
            lastName = lastName.ToTitleCase().Trim();
            personCode = personCode.Trim();

            CurrentUser.FirstName = firstName;
            CurrentUser.LastName = lastName;
            CurrentUser.PersonCode = personCode;
            CurrentUser.AuthenticationSource = data.authenticationProvider.ToString();
            CurrentUser.IsViispConfirmed = true;
        }

        public void UpdateAllPictures()
        {
            using (var session = noSqlSessionFactory())
            {
                foreach (var user in session.GetAll<Data.MongoDB.User>())
                {
                    if (!string.IsNullOrEmpty(user.ProfilePictureId) && string.IsNullOrEmpty(user.ProfilePictureThumbId))
                    {
                        var picture = session.GetFile<Data.MongoDB.User>(user.ProfilePictureId);
                        var thumb = PictureProcessor.ResizeImageFile(picture.File, 50);

                        var thumbId = session.SaveFile("ProfilePictureThumb_" + user.Id, thumb, MediaTypeNames.Image.Jpeg);
                        user.ProfilePictureThumbId = thumbId;
                        session.Update(user);
                        continue;
                    }

                    var userInfo = GetUserInfoByUserName(user.UserName);
                    if (userInfo != null && userInfo.FacebookId.HasValue)
                    {
                        SaveFacebookPicture(user.Id, userInfo.FacebookId.Value, false);
                        if (!user.WebSites.Any())
                        {
                            AddFacebookWebsite(user, userInfo.FacebookId.Value);
                        }

                        session.Update(user);
                    }
                }
            }
        }

        public List<int> GetPoliticianIds()
        {
            using (var context = usersContextFactory.CreateContext())
            {
                return context.Users.Where(u => u.IsPolitician).Select(u => new {u.Id}).FromCache().Select(u => u.Id).ToList();
            }
        }

        public bool Politician(string userObjectId, bool isPolitician)
        {
            using (var context = usersContextFactory.CreateContext(true))
            {
                var user = context.Users.Single(u => u.ObjectId == userObjectId);
                user.IsPolitician = isPolitician;
                context.Users.Where(u => u.IsPolitician).Select(u => new {u.Id}).RemoveCache();
                return true;
            }
        }

        public int GetMembersCount()
        {
            using (var context = usersContextFactory.CreateContext(true))
            {
                return context.Users.Count();
            }
        }

        public string GetMembersCountString(int count)
        {
            return GlobalizedSentences.GetMembersCountString(count);
        }

        public bool CanSendMail()
        {
            if (CurrentUser.Role == UserRoles.Admin)
            {
                return true;
            }

            var user = GetUser(CurrentUser.Id);
            if (user.LastMailSendDate.HasValue && user.LastMailSendDate.Value.AddDays(1) > DateTime.Now)
            {
                return false;
            }

            return true;
        }

        public void SetMailSendDate()
        {
            var user = GetUser(CurrentUser.Id);
            user.LastMailSendDate = DateTime.Now;
            UpdateUser(user);
        }

        public ContactsEditModel FillContactsModel(ContactsEditModel model)
        {
            if (model.WebSites == null)
            {
                model.WebSites = new List<UrlEditModel>();
            }

            return model;
        }

        public List<string> GetUserEmails(int userId)
        {
            using (var session = usersContextFactory.CreateContext())
            {
                return session.UserEmails.Where(e => e.UserId == userId && e.IsEmailConfirmed && e.SendMail).Select(u => u.Email).ToList();
            }
        }

        public List<int> GetNewsLetterUserIds()
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                var lastDay = DateTime.Now.AddDays(-1);
                var lastWeek = DateTime.Now.AddDays(-7);
                var lastMonth = DateTime.Now.AddMonths(-1);
                return noSqlSession.GetAll<Data.MongoDB.User>().Where(u => 
                    (u.Settings.NewsLetterFrequency == NewsLetterFrequency.Daily && (u.LastNewsLetterSendDate == null || u.LastNewsLetterSendDate < lastDay)) ||
                    (u.Settings.NewsLetterFrequency == NewsLetterFrequency.Weekly && (u.LastNewsLetterSendDate == null || u.LastNewsLetterSendDate < lastWeek)) ||
                    (u.Settings.NewsLetterFrequency == NewsLetterFrequency.Monthly && (u.LastNewsLetterSendDate == null || u.LastNewsLetterSendDate < lastMonth))
                    ).Select(u => u.DbId).ToList();
            }
        }

        public void UpdateNewsLetterDate(int userId)
        {
            using (var noSqlSession = noSqlSessionFactory())
            {
                var user = noSqlSession.GetAll<Data.MongoDB.User>().SingleOrDefault(u => u.DbId == userId);
                if (user != null)
                {
                    user.LastNewsLetterSendDate = DateTime.Now;
                    noSqlSession.Update(user);
                }
            }
        }

        public void SendUserDeleteMessage(UserDeleteRequestViewModel model)
        {
            using (var userSession = usersContextFactory.CreateContext())
            {
                var admins = userSession.Users.Where(u => u.Role == (short) UserRoles.Admin).ToList();
                foreach (var admin in admins)
                {
                    SendMessage(admin.ObjectId, string.Format("Naudotojo FullName={0}, UserName={1}, Id={2}, DbId={3} paskyra pašalinta. <br/>Pašalinimo priežastis: {4}<br/>Komentaras: {5}", CurrentUser.FullName, CurrentUser.UserName, CurrentUser.Id, CurrentUser.DbId, UserDeleteReasons.ResourceManager.GetString(model.Reason.ToString()), model.Comment),
                        CurrentUser.Email, "Paskyra pašalinta");
                }
            }
        }
    }
}
