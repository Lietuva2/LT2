using System.Web.Routing;
using Bus.CommandHandler;
using Data.EF.Actions;
using Data.EF.Users;
using Data.EF.Voting;
using Data.Infrastructure.Sessions;
using Framework.Bus;
using Framework.Infrastructure.Logging;
using Framework.Infrastructure.Notification;
using Hangfire;
using Microsoft.AspNet.SignalR;
using Services.Caching;
using Services.ModelServices;
using Web.Infrastructure.Authentication;
using Ninject.Extensions.Conventions;
using Services.Infrastructure;
using Web.Infrastructure.Hubs;
using Web.Infrastructure.SignalR;

[assembly: WebActivator.PreApplicationStartMethod(typeof(Web.App_Start.NinjectWebCommon), "Start")]
[assembly: WebActivator.ApplicationShutdownMethodAttribute(typeof(Web.App_Start.NinjectWebCommon), "Stop")]

namespace Web.App_Start
{
    using System;
    using System.Web;

    using Microsoft.Web.Infrastructure.DynamicModuleHelper;

    using Ninject;
    using Ninject.Web.Common;

    public static class NinjectWebCommon 
    {
        internal static readonly Bootstrapper Bootstrapper = new Bootstrapper();

        /// <summary>
        /// Starts the application
        /// </summary>
        public static void Start() 
        {
            DynamicModuleUtility.RegisterModule(typeof(OnePerRequestHttpModule));
            DynamicModuleUtility.RegisterModule(typeof(NinjectHttpModule));
            Bootstrapper.Initialize(CreateKernel);
            //ConfigureServiceBus(bootstrapper.Kernel);
        }
        
        /// <summary>
        /// Stops the application.
        /// </summary>
        public static void Stop()
        {
            Bootstrapper.ShutDown();
        }
        
        /// <summary>
        /// Creates the kernel that will manage your application.
        /// </summary>
        /// <returns>The created kernel.</returns>
        private static IKernel CreateKernel()
        {
            var kernel = new StandardKernel();
            kernel.Bind<Func<IKernel>>().ToMethod(ctx => () => new Bootstrapper().Kernel);
            kernel.Bind<IHttpModule>().To<HttpApplicationInitializationHttpModule>();
            RegisterServices(kernel);

            return kernel;
        }

        /// <summary>
        /// Load your modules or register your services here!
        /// </summary>
        /// <param name="kernel">The kernel.</param>
        private static void RegisterServices(IKernel kernel)
        {
            NinjectBindings.RegisterServices(kernel);
            kernel.Bind<IAuthentication>().To<OwinAuthentication>();
            kernel.Bind<IUserIdProvider>().To<ChatUserIdProvider>();
            kernel.Bind<ChatHub>().ToSelf();
            kernel.Bind<IBus>().ToMethod(x => new HangfireBus());
            kernel.Load<CommandHandlerModule>();
            GlobalConfiguration.Configuration.UseNinjectActivator(kernel);
        }        
    }
}
