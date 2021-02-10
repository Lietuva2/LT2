namespace Framework.Bus
{
    public abstract class CommandHandler<T> : BaseHandler, ICommandHandler<T> where T : Command
    {
        public abstract void Handle(T command);
    }
}
