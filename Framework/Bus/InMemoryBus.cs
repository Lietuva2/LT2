using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Framework.Bus
{
    public class InMemoryBus : IBus
    {
        protected readonly Func<Type, object> SingleInstanceResolver;

        public InMemoryBus(Func<Type, object> singleInstanceResolver)
        {
            this.SingleInstanceResolver = singleInstanceResolver;
        }

        public void Send<T>(T command) where T : Command
        {
            var handler = (ICommandHandler<T>)this.SingleInstanceResolver(typeof(ICommandHandler<T>));
            handler.Handle(command);
        }
    }
}
