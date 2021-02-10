
namespace EmailSender
{
    public class NinjectBindings : Ninject.Modules.NinjectModule
    {
        public override void Load()
        {
            //Bind<ILogger>().To<Log4NetLogger>().InSingletonScope();
            //Bind<Func<INoSqlSession>>().ToMethod(context => () => Kernel.Get<SiteMongoDbSession>());
            //Bind<IUsersContextFactory>().To<UsersContextFactory>();
            //Bind<IActionsContextFactory>().To<ActionsContextFactory>();
            //Bind<IVotingContextFactory>().To<VotingContextFactory>();
            //Bind<Func<IReporting>>().ToMethod(context => () => Kernel.Get<ReportingSession>());
            //Bind<ICache>().To<DefaultCache>();
            //Bind<NewsFeedService>().ToSelf();
            //Bind<IBus>().To<NullBus>();
            //Bind<IMailSender>().To<MailSender>();
            //Kernel.Bind(x => x.FromAssembliesMatching("Services")
            //    .SelectAllClasses()
            //    .InNamespaces("Services.ModelServices")
            //    .BindDefaultInterfaces());
        }
    }
}
