using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hangfire;

namespace Framework.Bus
{
    public class HangfireBus : IBus
    {
        public HangfireBus()
        {
        }

        public void Send<T>(T command) where T : Command
        {
            BackgroundJob.Enqueue<ICommandHandler<T>>(c => c.Handle(command));
        }
    }
}
