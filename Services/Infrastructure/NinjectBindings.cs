using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Data.EF.Actions;
using Data.EF.Users;
using Data.EF.Voting;
using Data.Infrastructure.Sessions;
using Framework.Infrastructure.Logging;
using Framework.Infrastructure.Notification;
using Ninject;
using Services.Caching;
using Ninject.Extensions.Conventions;

namespace Services.Infrastructure
{
    public class NinjectBindings
    {
        public static void RegisterServices(IKernel kernel)
        {
            kernel.Bind<ILogger>().To<Log4NetLogger>().InSingletonScope();
            kernel.Bind<ICache>().To<DefaultCache>();
            kernel.Bind<IMailSender>().To<MailSender>();
            kernel.Bind<IUsersContextFactory>().To<UsersContextFactory>();
            kernel.Bind<IActionsContextFactory>().To<ActionsContextFactory>();
            kernel.Bind<IVotingContextFactory>().To<VotingContextFactory>();
            kernel.Bind<Func<INoSqlSession>>().ToMethod(context => () => kernel.Get<SiteMongoDbSession>());
            kernel.Bind<Func<IReporting>>().ToMethod(context => () => kernel.Get<ReportingSession>());
        }
    }
}
