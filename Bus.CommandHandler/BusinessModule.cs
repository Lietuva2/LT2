using Framework.Bus;
using Ninject.Extensions.Conventions;
using Ninject.Modules;

namespace Bus.CommandHandler
{
    public class CommandHandlerModule : NinjectModule
    {
        public override void Load()
        {
            this.Bind(q => q.FromThisAssembly().SelectAllClasses().InheritedFrom(typeof(ICommandHandler<>)).BindAllInterfaces());
        }
    }
}
