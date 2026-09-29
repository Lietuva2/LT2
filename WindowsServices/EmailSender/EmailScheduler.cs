using System;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Data.EF.Users;
using Data.Enums;
using EmailSender;
using Framework.Bus;
using Framework.Infrastructure;
using Framework.Infrastructure.Notification;
using Framework.Infrastructure.Storage;
using Framework.Strings;
using Globalization;
using log4net.Config;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using Ninject;

using Scheduler.Base;
using Services.Infrastructure;
using Services.ModelServices;
using Services.Notifications;
using NinjectBindings = Services.Infrastructure.NinjectBindings;

namespace NDNT.EmailSender
{
    /// <summary>
    /// Schedules email sending.
    /// </summary>
    public class EmailScheduler : SchedulerBase<EmailScheduler>
    {
        /// <summary>
        /// Name of the instance.
        /// </summary>
        public const string Name = "LT2.EmailSender";

        private NewsFeedService newsFeedService;
        private UserService userService;
        private IUsersContextFactory usersContextFactory;
        private NewsLetterNotification notification;

        /// <summary>
        /// Initializes a new instance of the <see cref="EmailScheduler"/> class.
        /// </summary>
        public EmailScheduler(NewsFeedService newsFeedService, UserService userService, IUsersContextFactory usersContextFactory, NewsLetterNotification notification)
            : base(Name)
        {
            this.newsFeedService = newsFeedService;
            this.userService = userService;
            this.usersContextFactory = usersContextFactory;
            this.notification = notification;
        }

        /// <summary>
        /// Gets the time span between synchronizations in miliseconds.
        /// Default is 10 minutes
        /// </summary>
        /// <value>The time span.</value>
        public override int TimeSpan
        {
            get
            {
                return Int32.Parse(ConfigurationManager.AppSettings["TimeSpan"] ?? DefaultTimeSpan.ToString());
            }
        }

        /// <summary>
        /// Sends the email.
        /// </summary>
        public void SendEmail()
        {
            var culture = new System.Globalization.CultureInfo("lt-LT");
            if (culture.TwoLetterISOLanguageName.ToLower().Equals("lt"))
            {
                culture.DateTimeFormat.ShortDatePattern = "yyyy-MM-dd";
                culture.NumberFormat.NumberGroupSeparator = ".";
                culture.NumberFormat.NumberDecimalSeparator = ",";
                culture.NumberFormat.CurrencyGroupSeparator = ".";
                culture.NumberFormat.CurrencyDecimalSeparator = ",";
            }

            var thread = System.Threading.Thread.CurrentThread;
            thread.CurrentCulture = thread.CurrentUICulture = culture;

            Log.Information("Sending email started...");

            try
            {
                SendMyNewsEmails();
            }
            catch (Exception e)
            {
                Log.Error("Error sending email", e);
            }
            finally
            {
                Log.Information("Sending email finished...");
            }
        }

        /// <summary>
        /// Sends the query email.
        /// </summary>
        private void SendMyNewsEmails()
        {
            var stopwatchEnabled = Convert.ToBoolean(ConfigurationManager.AppSettings["EnableStopWatch"] ?? "false");
            var threshold = Convert.ToInt32(ConfigurationManager.AppSettings["Threshold"] ?? "10");
            var watch = Stopwatch.StartNew();
            var globalWatch = Stopwatch.StartNew();
            if (!stopwatchEnabled)
            {
                watch.Stop();
                globalWatch.Stop();
            }
            
            var userIds = userService.GetNewsLetterUserIds();

            if (stopwatchEnabled)
            {
                watch.Stop();
                if (watch.ElapsedMilliseconds > threshold)
                {
                    Log.Information("GetNewsLetterUserIds elapsed time: " + watch.ElapsedMilliseconds);
                }
            }
            
            Log.Information(string.Format("Preparing to send email to {0} users...", userIds.Count));
            int userCount = 0;
            foreach (var userId in userIds)
            {
                if (stopwatchEnabled)
                {
                    watch.Restart();
                }
                var emails = userService.GetUserEmails(userId);
                if (stopwatchEnabled)
                {
                    watch.Stop();
                    if (watch.ElapsedMilliseconds > threshold)
                    {
                        Log.Information(string.Format("GetUserEmails elapsed time: {0}, userId={1}", watch.ElapsedMilliseconds, userId));
                    }
                }

                if (emails.Count == 0)
                {
                    continue;
                }

                if (stopwatchEnabled)
                {
                    watch.Restart();
                }
                var count = newsFeedService.GetUnreadNewsCount(userId);
                if (stopwatchEnabled)
                {
                    watch.Stop();
                    if (watch.ElapsedMilliseconds > threshold)
                    {
                        Log.Information(string.Format("GetUnreadNewsCount elapsed time: {0}, userId={1}", watch.ElapsedMilliseconds, userId));
                    }
                }

                if (count == 0)
                {
                    continue;
                }

                
                var user = userService.GetUser(userId);
                if (stopwatchEnabled)
                {
                    watch.Restart();
                }
                var list = newsFeedService.GetNewsFeedPage(userId, user.LastNewsLetterSendDate).List.List.Where(l => !l.IsRead).ToList();
                if (stopwatchEnabled)
                {
                    watch.Stop();
                    if (watch.ElapsedMilliseconds > threshold)
                    {
                        Log.Information(string.Format("GetNewsFeedPage elapsed time: {0}, userId={1}", watch.ElapsedMilliseconds, userId));
                    }

                    watch.Restart();
                }
                if (!list.Any())
                {
                    continue;
                }

                if (count < list.Count())
                {
                    count = list.Count();
                }
                
                foreach (var email in emails)
                {
                    notification.To = email;
                    notification.News = string.Empty;
                    notification.NewsCount = count;
                    notification.NewsCountText = GlobalizedSentences.GetNewsCountText(count);
                    notification.NewsLetterFreq = Globalization.Resources.Services.NewsLetterFreq.ResourceManager.GetString(user.Settings.NewsLetterFrequency.ToString()).ToLower();

                    foreach (var item in list)
                    {
                        notification.News += Web.Helpers.SpecificHtmlHelpers.GetNewsFeedEntry(item) + "<br/>" +
                                             (item.Problem != null ? item.Subject : "") + "<br/>" + item.Text.NewLineToHtml();
                        notification.News += "<br/><br/>";
                    }


                    using (var session = usersContextFactory.CreateContext())
                    {
                        var not = session.Notifications.Single(n => n.Id == (int)NotificationTypes.NewsLetter);
                        notification.MessageTemplate = not.Message;
                        notification.Subject = not.Subject;
                        if (notification.Execute())
                        {
                            userService.UpdateNewsLetterDate(userId);
                            userCount++;
                        }
                    }
                }

                if (stopwatchEnabled)
                {
                    watch.Stop();
                    if (watch.ElapsedMilliseconds > threshold)
                    {
                        Log.Information(string.Format("Send mail elapsed time: {0}, userId={1}", watch.ElapsedMilliseconds, userId));
                    }
                }
            }

            if (stopwatchEnabled)
            {
                globalWatch.Stop();
                Log.Information("Total elapsed time: " + globalWatch.ElapsedMilliseconds);
            }

            Log.Information(string.Format("Sent {0} emails...", userCount));
        }

        /// <summary>
        /// Executes this instance.
        /// </summary>
        protected override void Execute()
        {
            SendEmail();
        }

        /// <summary>
        /// Main entry of the program.
        /// </summary>
        public static void Main()
        {
            MongoObjectIdGenerator.Register();
            MongoObjectIdSerializer.Register();

            BsonClassMap.RegisterConventions(new ConventionProfile().SetSerializationOptionsConvention(new LocalDateTimeSerializationConvention()), c => true);

            IKernel kernel = new StandardKernel();
            NinjectBindings.RegisterServices(kernel);
            ServiceLocator.Resolver = kernel.GetService;

            kernel.Bind<EmailScheduler>().ToSelf();
            kernel.Bind<IBus>().To<NullBus>();
            //kernel.Load(Assembly.GetExecutingAssembly());

            var sheduller = kernel.Get<EmailScheduler>();
#if DEBUG
            sheduller.OnStart(null);
            Console.ReadKey();
#else
            try
            {
                System.ServiceProcess.ServiceBase.Run(sheduller);
            }
            catch (Exception e)
            {
                Log.Information(String.Format("Starting service failed: {0}.", e));
            }
#endif
        }
    }    
}

