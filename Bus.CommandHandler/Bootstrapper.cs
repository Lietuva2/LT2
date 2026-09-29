using System;
using System.Configuration;
using Bus.Commands;
using Data.EF.Actions;
using Data.EF.Users;
using Data.EF.Voting;
using Data.Infrastructure.Sessions;
using Framework.Infrastructure.Logging;
using Framework.Infrastructure.Notification;

using Ninject;
using Ninject.Extensions.Conventions;
using Services.Caching;
using Services.Infrastructure;
using Services.ModelServices;

namespace Bus.CompletedCommandHandler
{
    /// <summary>
    /// Dependency injection bootstrapper.
    /// </summary>
    public static class Bootstrapper
    {
        /// <summary>
        /// Configures all dependencies.
        /// </summary>
        /// <returns>Configured dependency injection container.</returns>
        public static void RegisterServices(IKernel kernel)
        {
            NinjectBindings.RegisterServices(kernel);
            //IKernel kernel = new StandardKernel();
            //kernel.Bind<ILogger>().To<Log4NetLogger>().InSingletonScope();
            //kernel.Bind<ICache>().To<DefaultCache>();
            //kernel.Bind<IMailSender>().To<MailSender>();
            //kernel.Bind<IUsersContextFactory>().To<UsersContextFactory>();
            //kernel.Bind<IActionsContextFactory>().To<ActionsContextFactory>();
            //kernel.Bind<IVotingContextFactory>().To<VotingContextFactory>();
            //kernel.Bind<Func<INoSqlSession>>().ToMethod(context => () => kernel.Get<SiteMongoDbSession>()).InTransientScope();
            //kernel.Bind<Func<IReporting>>().ToMethod(context => () => kernel.Get<ReportingSession>());
            //kernel.Bind(x => x.FromAssembliesMatching("Services")
            //    .SelectAllClasses()
            //    .InNamespaces("Services.ModelServices")
            //    .BindDefaultInterfaces());

            //return kernel;
        }
    }
}