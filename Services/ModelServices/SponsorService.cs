using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Mvc;
using Bus.Commands;
using Data.EF.Actions;
using Data.EF.Users;
using Data.Enums;
using Data.Infrastructure.Sessions;
using Data.ViewModels.Sponsor;
using Framework;
using Framework.Bus;
using Framework.Hashing;
using Framework.Infrastructure;
using Framework.Infrastructure.Logging;
using Framework.Lists;
using Framework.Mvc.Lists;

using PagedList;
using Services.Caching;
using Services.Session;

namespace Services.ModelServices
{
    public class SponsorService : IService
    {
        private IUsersContextFactory usersSessionFactory;
        private OrganizationService organizationService;
        private Func<INoSqlSession> noSqlSessionFactory;
        private IActionsContextFactory actionSessionFactory;
        private readonly ICache cache;
        private readonly IBus bus;
        private readonly ILogger logger;

        private int ItemsCount { get { return 10; } }

        public UserInfo CurrentUser { get { return MembershipSession.GetUser(); } }
        public UserService UserService { get { return ServiceLocator.Resolve<UserService>(); } }

        public SponsorService(
            IUsersContextFactory usersSessionFactory,
            Func<INoSqlSession> mongoDbSessionFactory,
            IActionsContextFactory actionSessionFactory,
            OrganizationService organizationService,
            ICache cache,
            IBus bus,
            ILogger logger)
        {
            this.usersSessionFactory = usersSessionFactory;
            this.noSqlSessionFactory = mongoDbSessionFactory;
            this.actionSessionFactory = actionSessionFactory;
            this.organizationService = organizationService;
            this.cache = cache;
            this.bus = bus;
            this.logger = logger;
        }

        public void ImportExcelData(BankAccountModel.AccountModel model)
        {
            using (var session = usersSessionFactory.CreateContext(true))
            {
                var account =
                    session.BankAccounts.SingleOrDefault(a => a.AccountNo == model.AccountNo);
                if (account == null)
                {
                    account = new BankAccount()
                    {
                        AccountNo = model.AccountNo.Trim()
                    };
                    session.BankAccounts.Add(account);
                }

                account.Balance = (decimal)model.Balance;

                var lastDbDate = account.BankAccountItems.Any() ? account.BankAccountItems.Max(i => i.Date) : DateTime.MinValue;

                foreach (var item in model.Items)
                {
                    if (item.Date > lastDbDate)
                    {
                        var operation = item.Operation;
                        var cutAt = ", mok. įm. / a. k.";
                        if (operation.Contains(cutAt))
                        {
                            operation = operation.Substring(0, operation.IndexOf(cutAt));
                        }

                        cutAt = ", gav. įm. / a. k.";
                        if (operation.Contains(cutAt))
                        {
                            operation = operation.Substring(0, operation.IndexOf(cutAt));
                        }

                        account.BankAccountItems.Add(new BankAccountItem()
                        {
                            Date = item.Date,
                            Operation = operation,
                            Expense = item.ExpenseDecimal,
                            Income = item.IncomeDecimal
                        });
                    }
                }
            }
        }

        public BankAccountModel GetAccountModel(int pageNumber, int? accountId, int webToPayPageNumber, int pageSize)
        {
            using (var session = usersSessionFactory.CreateContext())
            {
                var accounts = (from a in session.BankAccounts
                                select new BankAccountModel.AccountModel()
                                {
                                    Id = a.Id,
                                    AccountNo = a.AccountNo,
                                    Balance = (double)a.Balance
                                }).ToList();

                accounts.ForEach(
                    x =>
                    {
                        x.Items = session.BankAccountItems.Where(q => q.BankAccountId == x.Id)
                            .OrderByDescending(q => q.Date)
                            .Select(i =>
                                new BankAccountItemModel()
                                {
                                    Id = i.Id,
                                    Date = i.Date,
                                    ExpenseDecimal = i.Expense,
                                    IncomeDecimal = i.Income,
                                    Operation = i.Operation,
                                    UserFullName = i.User.FirstName + " " + i.User.LastName,
                                    UserObjectId = i.User.ObjectId,
                                    OrganizationId = i.OrganizationId
                                }).ToPagedList(x.Id == accountId ? pageNumber : 1, pageSize);

                        x.Currency = x.AccountNo.Substring(x.AccountNo.IndexOf('(') + 1, x.AccountNo.IndexOf(')') - x.AccountNo.IndexOf('(') - 1);
                        x.Items.ForEach(
                            m =>
                                m.OrganizationName = organizationService.GetOrganizationName(m.OrganizationId)
                            );
                    });

                var model = new BankAccountModel()
                {
                    Accounts = accounts
                };

                model.WebToPayItems = (from i in session.WebToPayLogs
                                       where i.Status == 1
                                       orderby i.Date descending
                                       select new BankAccountItemModel()
                                       {
                                           Id = i.Id,
                                           Date = i.Date,
                                           ExpenseDecimal = null,
                                           IncomeDecimal = i.Amount / 100,
                                           Operation = i.Firstname + " " + i.LastName + " " + i.PayText,
                                           UserFullName = i.User.FirstName + " " + i.User.LastName,
                                           UserObjectId = i.User.ObjectId
                                       }).ToPagedList(webToPayPageNumber, pageSize);

                return model;
            }
        }

        public DonateModel GetDonateModel()
        {
            using (var session = usersSessionFactory.CreateContext())
            {
                Data.EF.Users.User user = null;
                if (CurrentUser.IsAuthenticated)
                {
                    user = session.Users.SingleOrDefault(u => u.Id == CurrentUser.DbId);
                }

                var model = new DonateModel()
                {
                    FirstName = user != null ? user.FirstName : null,
                    LastName = user != null ? user.LastName : null,
                    Email = user != null ? user.UserEmails.Where(e => e.SendMail && e.IsEmailConfirmed).Select(e => e.Email).FirstOrDefault() : null
                };

                return model;
            }
        }

        public bool UpdateRelatedUser(int itemId, int userId)
        {
            using (var session = usersSessionFactory.CreateContext(true))
            {
                var item = session.BankAccountItems.SingleOrDefault(i => i.Id == itemId);
                item.UserId = userId;
                AwardSponsor(userId);

                return true;
            }
        }

        public bool UpdateOperation(int itemId, string operation)
        {
            using (var session = usersSessionFactory.CreateContext(true))
            {
                var item = session.BankAccountItems.SingleOrDefault(i => i.Id == itemId);
                item.Operation = operation;

                return true;
            }
        }

        public void AwardSponsor(int userId)
        {
            using (var session = usersSessionFactory.CreateContext(true))
            {
                var award =
                    session.UserAwards.SingleOrDefault(
                        i => i.UserId == userId && i.AwardId == (short)UserAwards.Sponsor);
                if (award == null)
                {
                    award = new Data.EF.Users.UserAward()
                    {
                        UserId = userId,
                        AwardId = (short)UserAwards.Sponsor
                    };
                    session.UserAwards.Add(award);
                    var userObjectId =
                        session.Users.Where(u => u.Id == userId).Select(u => u.ObjectId).
                            SingleOrDefault();
                    bus.Send(new RelatedUserCommand()
                    {
                        ActionType = ActionTypes.UserAwarded,
                        MessageDate = DateTime.Now,
                        UserId = CurrentUser.IsAuthenticated ? CurrentUser.Id : userObjectId,
                        RelatedUserId = userObjectId,
                        Text =
                                Globalization.Resources.Services.UserAward.ResourceManager.GetString(
                                    UserAwards.Sponsor.ToString())
                    });
                }
            }
        }

        public WebToPayModel GetWebToPayModel(DonateModel model, string password, string projectId, string acceptUrl, string cancelUrl, string callbackUrl, string paytext, string test)
        {
            var orderid = GetNewOrderId(model);
            NameValueCollection nvc = new NameValueCollection(){
                {"projectid", projectId},
                {"orderid", orderid},
                {"lang", "LIT"},
                {"amount", model.Amount.ToString()},
                {"currency", "EUR"},
                {"accepturl", acceptUrl},
                {"cancelurl", cancelUrl},
                {"callbackurl", callbackUrl},
                {"payment", model.PaymentType},
                {"country", "LT"},
                {"paytext", paytext},
                {"p_firstname", model.FirstName},
                {"p_lastname", model.LastName},
                {"p_email", model.Email},
                {"p_street", ""},
                {"p_city", ""},
                {"p_state", ""},
                {"p_zip", ""},
                {"p_countrycode", ""},
                {"test", test},
                {"version", "1.6"},
                {"personcode", (model.PersonCode ?? string.Empty).Trim()}
            };

            var qs = ToQueryString(nvc);

            var data = Convert.ToBase64String(Encoding.UTF8.GetBytes(qs));
            data = data.Replace('/', '_').Replace('+', '-');


            var result = new WebToPayModel()
            {
                firstname = model.FirstName,
                lastname = model.LastName,
                email = model.Email,
                personcode = model.PersonCode,
                orderid = orderid,
                amount = model.Amount.ToString(),
                data = data,
                sign = (data + password).GetMd5Hash(),
                Gifts = GetAvailableGifts(model.Amount),
                PaymentType = model.PaymentType
            };

            return result;
        }

        private string ToQueryString(NameValueCollection nvc)
        {
            return string.Join("&", Array.ConvertAll(nvc.AllKeys, key => string.Format("{0}={1}", HttpUtility.UrlEncode(key), HttpUtility.UrlEncode(nvc[key]))));
        }

        private List<GiftModel> GetAvailableGifts(int amount)
        {
            using (var session = usersSessionFactory.CreateContext())
            {
                return session.Gifts.Where(g => g.RequiredAmount <= amount).Select(g => new GiftModel()
                {
                    Id = g.Id,
                    Name = g.Name,
                    Url = g.Url
                }).ToList();
            }
        }

        public string GetNewOrderId(DonateModel model)
        {
            using (var session = usersSessionFactory.CreateContext())
            {
                var order = new Data.EF.Users.WebToPayLog()
                {
                    Date = DateTime.Now,
                    UserId = CurrentUser.IsAuthenticated ? (int?)CurrentUser.DbId : null,
                    Firstname = model.FirstName,
                    LastName = model.LastName,
                    Email = model.Email,
                    Status = 0
                };

                session.WebToPayLogs.Add(order);
                session.SaveChanges();
                return order.Id.ToString();
            }
        }

        public bool CheckResponse(WebToPayResponseModel model, string password)
        {
            return (model.data + password).GetMd5Hash() == model.ss1;
        }

        public void ProcessPayment(WebToPayResponseModel model)
        {
            var nvc = ProcessData(model.data);
            logger.Information("Callback data: " + nvc);
            using (var session = usersSessionFactory.CreateContext(true))
            {
                var orderId = Convert.ToInt32(nvc["orderid"]);
                var status = Convert.ToInt16(nvc["status"]);
                var order = session.WebToPayLogs.Single(l => l.Id == orderId);
                order.Amount = Convert.ToDecimal(nvc["payamount"]);
                order.Country = nvc["country"];
                order.Currency = nvc["paycurrency"];
                order.Date = DateTime.Now;
                order.Email = nvc["p_email"];
                order.Firstname = nvc["name"];
                order.LastName = nvc["surename"];
                order.RequestId = nvc["requestid"];
                if (order.Status != 1)
                {
                    order.Status = status;
                }

                order.PayText = nvc["paytext"];

                if (order.UserId.HasValue)
                {
                    if (status == 1)
                    {
                        AwardSponsor(order.UserId.Value);
                    }

                    if (nvc["personcodestatus"] == "1")
                    {
                        using (var noSqlSession = noSqlSessionFactory())
                        {
                            var user =
                                noSqlSession.GetAll<Data.MongoDB.User>().Single(u => u.DbId == order.UserId.Value);
                            UserService.UniqueUser(order.Firstname, order.LastName, user.PersonCode, nvc["payment"],
                                                   order.UserId);
                        }
                    }

                    if (status == 3 && nvc["personcodestatus"] != null)
                    {
                        bus.Send(new UserConfirmedCommand()
                        {
                            UserId = order.UserId.Value,
                            PersonCodeStatus = Convert.ToInt32(nvc["personcodestatus"])
                        });
                    }
                }
            }
        }

        private NameValueCollection ProcessData(string data)
        {
            data = data.Replace('-', '+').Replace('_', '/');
            var q = Encoding.UTF8.GetString(Convert.FromBase64String(data));
            return HttpUtility.ParseQueryString(q);
        }

        public bool SaveGift(string orderId, int? giftId)
        {
            using (var session = usersSessionFactory.CreateContext(true))
            {
                int id = Convert.ToInt16(orderId);
                var order = session.WebToPayLogs.SingleOrDefault(l => l.Id == id);
                order.GiftId = giftId;
            }

            return true;
        }

        public void SavePersonCode(string personCode)
        {
            if (!CurrentUser.IsAuthenticated)
            {
                return;
            }
            using (var session = noSqlSessionFactory())
            {
                var user = session.GetAll<Data.MongoDB.User>().Single(u => u.Id == CurrentUser.Id);
                user.PersonCode = personCode;
                session.Update(user);
            }
        }

        public PaymentAcceptModel GetPaymentAcceptModel(WebToPayResponseModel model)
        {
            var nvc = ProcessData(model.data);
            return new PaymentAcceptModel()
            {
                Success = nvc["status"] == "1",
                PersonCodeStatus = Convert.ToInt32(nvc["personcodestatus"])
            };
        }
    }
}
