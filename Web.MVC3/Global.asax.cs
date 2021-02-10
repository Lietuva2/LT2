using System;
using System.Configuration;
using System.Linq;
using System.Reflection;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using System.Web.WebPages;
using Framework.Bus;
using Framework.Enums;
using Framework.Infrastructure;
using Framework.Infrastructure.Logging;
using Framework.Infrastructure.Storage;
using Framework.Mvc;
using Microsoft.AspNet.SignalR;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Options;

using Services.Infrastructure;
using Services.ModelServices;
using Services.Session;
using Web.App_Start;
using Web.Infrastructure.Attributes;
using Web.Infrastructure.Bundles;
using Web.Infrastructure.Meta;
using Web.Infrastructure.Routing;
using Web.Infrastructure.SignalR;

namespace Web
{
    // Note: For instructions on enabling IIS6 or IIS7 classic mode, 
    // visit http://go.microsoft.com/?LinkId=9394801

    public class MvcApplication : System.Web.HttpApplication
    {
        /// <summary>
        /// This is here for you to use as needed. Application.Environment...
        /// </summary>
        public static AppEnvironment Environment
        {
            get
            {
#if DEBUG
                return AppEnvironment.Development;
#else
                
#endif
                return AppEnvironment.Production;
            }
        }

        public static ILogger Logger { get; private set; }

        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
        }

        public static void RegisterRoutes(RouteCollection routes)
        {
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

            routes.MapRoute(
                "Start", // Route name
                "lt", // URL with parameters
                new { controller = "NewsFeed", action = "Default", lang = "lt-LT" } // Parameter defaults
            );

            routes.MapRoute(
                "StartEn", // Route name
                "en", // URL with parameters
                new { controller = "NewsFeed", action = "Default", lang = "en-US" } // Parameter defaults
            );

            routes.MapRoute(
                "Login", // Route name
                "login", // URL with parameters
                new { controller = "Account", action = "Login" } // Parameter defaults
            );
            routes.MapRoute(
                "Logout", // Route name
                "logout", // URL with parameters
                new { controller = "Account", action = "Logout" } // Parameter defaults
            );

            routes.MapRoute(
                "versloidejos", // Route name
                "versloidejos", // URL with parameters
                new { controller = "Idea", action = "Index", categoryId = 32 }, // Parameter defaults
                new { categoryId = "32" }
            );

            routes.MapRoute(
                "Ideas", // Route name
                "pasiulymai", // URL with parameters
                new { controller = "Idea", action = "Index" } // Parameter defaults
            );

            routes.MapRoute(
                "Voting", // Route name
                "sprendimai", // URL with parameters
                new { controller = "Voting", action = "Index" } // Parameter defaults
            );

            routes.MapRoute(
                "Results", // Route name
                "rezultatai", // URL with parameters
                new { controller = "Voting", action = "Results" } // Parameter defaults
            );

            routes.MapRoute(
                "Prioritizer", // Route name
                "prioritetu-pasirinkimas", // URL with parameters
                new { controller = "Idea", action = "Prioritizer" } // Parameter defaults
            );

            routes.MapRoute(
                "PrioritizerResults", // Route name
                "prioritetai", // URL with parameters
                new { controller = "Idea", action = "PrioritizerResults" } // Parameter defaults
            );

            routes.MapRoute(
                "MyNewsFeed", // Route name
                "naujienos/aktualios", // URL with parameters
                new { controller = "NewsFeed", action = "Index", listView = "MyNews" }
            );

            routes.MapRoute(
                "AllNewsFeed", // Route name
                "naujienos/visos", // URL with parameters
                new { controller = "NewsFeed", action = "Index", listView = "AllNews" }
            );

            routes.MapRoute(
                "PolNewsFeed", // Route name
                "politiku-tribuna", // URL with parameters
                new { controller = "NewsFeed", action = "Index", listView = "PolNews" }
            );

            routes.MapRoute(
                "NewsFeed", // Route name
                "naujienos", // URL with parameters
                new { controller = "NewsFeed", action = "Index" } // Parameter defaults
            );

            routes.MapRoute(
                "About", // Route name
                "apie", // URL with parameters
                new { controller = "About", action = "About" } // Parameter defaults
            );

            routes.MapRoute(
                "AboutNewsFeed", // Route name
                "apie/video", // URL with parameters
                new { controller = "About", action = "Video" } // Parameter defaults
            );

            routes.MapRoute(
                "AboutIdeas", // Route name
                "apie/vertybes", // URL with parameters
                new { controller = "About", action = "Ideas" } // Parameter defaults
            );

            routes.MapRoute(
                "AboutVoting", // Route name
                "apie/manifestas", // URL with parameters
                new { controller = "About", action = "Manifest" } // Parameter defaults
            );

            routes.MapRoute(
                "AboutRoadMap", // Route name
                "apie/ateitis", // URL with parameters
                new { controller = "About", action = "RoadMap" } // Parameter defaults
            );

            routes.MapRoute(
                "AboutContacts", // Route name
                "kontaktai", // URL with parameters
                new { controller = "About", action = "Contacts" } // Parameter defaults
            );

            routes.MapRoute(
                "ShortLink", // Route name
                "{id}", // URL with parameters
                new { controller = "Common", action = "ResolveShortLink" }, // Parameter defaults
                new { id = new ShortLinkRouteConstraint() }
            );

            routes.MapRoute(
                "IdeaSubjectVersion", // Route name
                "pasiulymas/{subject}/{id}/{versionId}", // URL with parameters
                new { controller = "Idea", action = "Details", subject = UrlParameter.Optional },// Parameter defaults
                new { id = @"^(?=.*\d)\w{24}$", versionId = @"^(?=.*\d)\w{24}$" }
            );
            
            routes.MapRoute(
                "IdeaSubject", // Route name
                "pasiulymas/{subject}/{id}", // URL with parameters
                new { controller = "Idea", action = "Details", subject = UrlParameter.Optional  },// Parameter defaults
                new { id = @"^(?=.*\d)\w{24}$" }
            );

            routes.MapRoute(
                "IssueSubject", // Route name
                "sprendimas/{subject}/{id}", // URL with parameters
                new { controller = "Voting", action = "Details", subject = UrlParameter.Optional }, // Parameter defaults
                new { id = @"^(?=.*\d)\w{24}$" }
            );

            routes.MapRoute(
                "Idea", // Route name
                "pasiulymas/{id}", // URL with parameters
                new { controller = "Idea", action = "Details" },// Parameter defaults
                new { id = @"^(?=.*\d)\w{24}$" }
            );

            routes.MapRoute(
                "Issue", // Route name
                "sprendimas/{id}", // URL with parameters
                new { controller = "Voting", action = "Details" }, // Parameter defaults
                new { id = @"^(?=.*\d)\w{24}$" }
            );

            routes.MapRoute(
                "Project", // Route name
                "Projektas/{projectId}", // URL with parameters
                new { controller = "Project", action = "ToDos", projectId = UrlParameter.Optional }, // Parameter defaults
                new { projectId = @"^(?=.*\d)\w{24}$" }
            );

            routes.MapRoute(
                "OrganizationProject", // Route name
                "Organizacija/{organizationId}/Projektas/{projectId}", // URL with parameters
                new { controller = "Organization", action = "ToDos", organizationId = UrlParameter.Optional, projectId = UrlParameter.Optional }, // Parameter defaults
                new { projectId = @"^(?=.*\d)\w{24}$", organizationId = @"^(?=.*\d)\w{24}$" }
            );

            routes.MapRoute(
                "Organization", // Route name
                "Organizacija/{name}/{objectId}", // URL with parameters
                new { controller = "Organization", action = "Details", name = UrlParameter.Optional }, // Parameter defaults
                new { objectId = @"^(?=.*\d)\w{24}$" }
            );

            routes.MapRoute(
                "ProjectAction", // Route name
                "Projektas/{action}/{projectId}", // URL with parameters
                new { controller = "Project", action = "ToDos", projectId = UrlParameter.Optional }, // Parameter defaults
                new { projectId = @"^(?=.*\d)\w{24}$" }
            );
            
            routes.MapRoute(
                "UserFullName", // Route name
                "{fullName}/{userObjectId}", // URL with parameters
                new { controller = "Account", action = "Details" }, // Parameter defaults
                new { userObjectId = @"^(?=.*\d)\w{24}$" }
            );

            routes.MapRoute(
                "User", // Route name
                "pilietis/{userObjectId}", // URL with parameters
                new { controller = "Account", action = "Details" }, // Parameter defaults
                new { userObjectId = @"^(?=.*\d)\w{24}$" }
            );

            routes.MapRoute(
                "RawUser", // Route name
                "pilietis", // URL with parameters
                new { controller = "Account", action = "Details" } // Parameter defaults
            );

            routes.MapRoute(
                "DonatasSimelis", // Route name
                "donatas-simelis", // URL with parameters
                new { controller = "Account", action = "Details", userObjectId = "9ce63302c79d1eb80d000000", view = "Activity", fullName = "donatas-šimelis" }
            );

            routes.MapRoute(
                "EmailConfirm", // Route name
                "ConfirmEmail/{code}", // URL with parameters
                new { controller = "Account", action = "ConfirmEmail" }
            );

            routes.MapRoute(
                "Portfolio", // Route name
                "Martynas_Simelis/Darbai", // URL with parameters
                new { controller = "Martynas_Simelis", action = "Portfolio" }, // Parameter defaults
                new { id = @"^(?=.*\d)\w{24}$" }
            );

            routes.MapRoute(
                "Award", // Route name
                "Martynas_Simelis/Stipendija", // URL with parameters
                new { controller = "Martynas_Simelis", action = "Award" } // Parameter defaults
            );

            routes.MapRoute(
                "Biography", // Route name
                "Martynas_Simelis/Biografija", // URL with parameters
                new { controller = "Martynas_Simelis", action = "Biography" } // Parameter defaults
            );

            routes.MapRoute(
                "Martynas", // Route name
                "Martynas_Simelis", // URL with parameters
                new { controller = "Martynas_Simelis", action = "Award" } // Parameter defaults
            );

            routes.MapRoute(
                "Vilnius", // Route name
                "Vilnius", // URL with parameters
                new { controller = "Address", action = "Municipality", name = "Vilnius" }, // Parameter defaults
                new { name = "Vilnius"}
            );

            routes.MapRoute(
                "Kaunas", // Route name
                "Kaunas", // URL with parameters
                new { controller = "Address", action = "Municipality", name = "Kaunas" }, // Parameter defaults
                new { name = "Kaunas" }
            );

            routes.MapRoute(
                "Klaipeda", // Route name
                "Klaipeda", // URL with parameters
                new { controller = "Address", action = "Municipality", name = "Klaipeda" }, // Parameter defaults
                new { name = "Klaipeda" }
            );

            routes.MapRoute(
                "Ukmerge", // Route name
                "Ukmerge", // URL with parameters
                new { controller = "Address", action = "Municipality", name = "Ukmerge" }, // Parameter defaults
                new { name = "Ukmerge" }
            );

            routes.MapRoute(
                "Region", // Route name
                "Savivaldybe/{name}", // URL with parameters
                new { controller = "Address", action = "Municipality" } // Parameter defaults
            );

            routes.MapRoute(
                "Default", // Route name
                "{controller}/{action}/{id}", // URL with parameters
                new { controller = "Common", action = "Start", id = UrlParameter.Optional } // Parameter defaults
            );

        }

        protected void Application_BeginRequest()
        {
        }

        protected void Application_EndRequest()
        {
        }

        protected void Application_Start()
        {
           // MiniProfilerEF6.Initialize();

            Logger = Log4NetLogger.Configure();
            Logger.Information("App is starting");

            AreaRegistration.RegisterAllAreas();

            RegisterGlobalFilters(GlobalFilters.Filters);
            RegisterRoutes(RouteTable.Routes);

            //RouteDebug.RouteDebugger.RewriteRoutesForTesting(RouteTable.Routes);

            DefaultModelBinder.ResourceClassKey = "ErrorMessages";

            ModelMetadataProviders.Current = new CustomModelMetadataProvider();

            MongoObjectIdGenerator.Register();
            MongoObjectIdSerializer.Register();
            BsonClassMap.RegisterConventions(new ConventionProfile().SetSerializationOptionsConvention(new LocalDateTimeSerializationConvention()), c => true);

            var kernel = new Ninject.Web.Common.Bootstrapper().Kernel;
            ServiceLocator.Resolver = kernel.GetService;

            BundleConfig.RegisterBundles(BundleTable.Bundles);

            ViewEngines.Engines.Clear();
            ViewEngines.Engines.Add(new LocalizedRazorViewEngine());
            ViewEngines.Engines.Add(new LocalizedWebFormViewEngine());
            
            //GlobalFilters.Filters.Add(new StackExchange.Profiling.Data..Profiling.MVCHelpers.ProfilingActionFilter());

            var chatService = ServiceLocator.Resolve<ChatService>();
            chatService.ClearClients();
        }
    }
}